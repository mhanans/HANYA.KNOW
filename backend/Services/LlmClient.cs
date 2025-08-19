using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class LlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<LlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        return _options.Provider.ToLower() switch
        {
            "gemini" => await CallGeminiAsync(prompt),
            _ => await CallOpenAiAsync(prompt)
        };
    }

    private async Task<string> CallOpenAiAsync(string prompt)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = _options.Model,
            messages = new[] { new { role = "user", content = prompt } }
        });
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async Task<string> CallGeminiAsync(string prompt)
    {
        var url = $"https://generativelanguage.googleapis.com/v1/models/{_options.Model}:generateContent?key={_options.ApiKey}";

        for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                // Gemini expects a role on each message; omit it and the API may return
                // an empty candidate without parts, which manifests as "response missing text".
                var res = await _http.PostAsJsonAsync(url, new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = prompt } } }
                    }
                });

                res.EnsureSuccessStatusCode();

                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
                {
                    var cand = candidates[0];
                    if (cand.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }

                // Log unexpected response for diagnostics
                _logger.LogError("Unexpected Gemini response: {Response}", root.GetRawText());

                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException($"Gemini API error: {error}");
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                // Swallow exception to allow retry
                _logger.LogWarning(ex, "Gemini call failed on attempt {Attempt}", attempt);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
        }

        throw new InvalidOperationException("Gemini response missing text");
    }
}
