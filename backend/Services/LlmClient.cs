using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
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

    public Task<string> GenerateAsync(string prompt)
        => GenerateAsync(new[] { new ChatMessage("user", prompt) });

    public async Task<string> GenerateAsync(IEnumerable<ChatMessage> messages)
    {
        return _options.Provider.ToLower() switch
        {
            "gemini" => await CallGeminiAsync(messages),
            _ => await CallOpenAiAsync(messages)
        };
    }

    public IAsyncEnumerable<string> GenerateStreamAsync(string prompt)
        => GenerateStreamAsync(new[] { new ChatMessage("user", prompt) });

    public async IAsyncEnumerable<string> GenerateStreamAsync(IEnumerable<ChatMessage> messages)
    {
        if (_options.Provider.ToLower() == "gemini")
        {
            var text = await GenerateAsync(messages);
            yield return text;
            yield break;
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = _options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true
        });

        var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: "))
            {
                var json = line[6..];
                if (json == "[DONE]")
                    yield break;

                using var doc = JsonDocument.Parse(json);
                var choice = doc.RootElement.GetProperty("choices")[0];
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return text;
                }
            }
        }
    }

    private async Task<string> CallOpenAiAsync(IEnumerable<ChatMessage> messages)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = _options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
        });
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async Task<string> CallGeminiAsync(IEnumerable<ChatMessage> messages)
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
                    contents = messages.Select(m => new { role = m.Role, parts = new[] { new { text = m.Content } } })
                });

                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Gemini API returned {StatusCode}: {Body}",
                        (int)res.StatusCode,
                        body);
                    res.EnsureSuccessStatusCode();
                }

                using var doc = JsonDocument.Parse(body);
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

                if (root.TryGetProperty("promptFeedback", out var feedback) &&
                    feedback.TryGetProperty("blockReason", out var reason))
                {
                    throw new InvalidOperationException(
                        $"Gemini blocked response: {reason.GetString()}");
                }

                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException($"Gemini API error: {error}");

                throw new InvalidOperationException("Gemini response missing text.");
            }
            catch (HttpRequestException ex) when (attempt < _options.MaxRetries)
            {
                var delay = ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    : TimeSpan.FromMilliseconds(500 * attempt);

                _logger.LogWarning(
                    ex,
                    "Connection to Gemini API not stable, retrying ({Attempt}/{Max})",
                    attempt,
                    _options.MaxRetries);

                await Task.Delay(delay);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                _logger.LogWarning(
                    ex,
                    "Connection to Gemini API not stable, retrying ({Attempt}/{Max})",
                    attempt,
                    _options.MaxRetries);

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
            }
        }

        throw new InvalidOperationException(
            $"Connection to Gemini API failed after {_options.MaxRetries} attempts. Please check connection to API server.");
    }
}
