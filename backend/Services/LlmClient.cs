using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class LlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;
    private readonly SettingsStore _settings;

    public LlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<LlmClient> logger, SettingsStore settings)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _settings = settings;
    }

    public Task<string> GenerateAsync(string prompt)
        => GenerateAsync(new[] { new ChatMessage("user", prompt) });

    public async Task<string> GenerateAsync(IEnumerable<ChatMessage> messages)
    {
        var messageList = messages.ToList();
        var effective = await GetEffectiveOptionsAsync();
        var provider = effective.Provider.ToLowerInvariant();

        return provider switch
        {
            "gemini" => await CallGeminiAsync(messageList, effective),
            "ollama" => await CallOllamaAsync(messageList, effective),
            _ => await CallOpenAiAsync(messageList, effective)
        };
    }

    public IAsyncEnumerable<string> GenerateStreamAsync(string prompt)
        => GenerateStreamAsync(new[] { new ChatMessage("user", prompt) });

    public async IAsyncEnumerable<string> GenerateStreamAsync(IEnumerable<ChatMessage> messages)
    {
        var messageList = messages.ToList();
        var effective = await GetEffectiveOptionsAsync();
        var provider = effective.Provider.ToLowerInvariant();

        if (provider is "gemini" or "ollama")
        {
            var text = provider == "gemini"
                ? await CallGeminiAsync(messageList, effective)
                : await CallOllamaAsync(messageList, effective);
            yield return text;
            yield break;
        }

        ValidateOpenAiOptions(effective);

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effective.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = effective.Model,
            messages = messageList.Select(m => new { role = m.Role, content = m.Content }),
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

    private async Task<LlmOptions> GetEffectiveOptionsAsync()
    {
        var settings = await _settings.GetAsync();
        var provider = string.IsNullOrWhiteSpace(settings.LlmProvider)
            ? _options.Provider
            : settings.LlmProvider;
        var model = string.IsNullOrWhiteSpace(settings.LlmModel)
            ? _options.Model
            : settings.LlmModel;
        var apiKey = string.IsNullOrEmpty(settings.LlmApiKey)
            ? _options.ApiKey
            : settings.LlmApiKey;
        var host = string.IsNullOrWhiteSpace(settings.OllamaHost)
            ? _options.OllamaHost
            : settings.OllamaHost;

        return new LlmOptions
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? "openai" : provider,
            Model = model ?? string.Empty,
            ApiKey = apiKey ?? string.Empty,
            OllamaHost = host ?? string.Empty,
            MaxRetries = _options.MaxRetries
        };
    }

    private static void ValidateOpenAiOptions(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("OpenAI model is not configured.");
    }

    private static void ValidateGeminiOptions(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("Gemini model is not configured.");
    }

    private static void ValidateOllamaOptions(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("Ollama model is not configured.");
        if (string.IsNullOrWhiteSpace(options.OllamaHost))
            throw new InvalidOperationException("Ollama host is not configured.");
        if (!Uri.TryCreate(options.OllamaHost, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Ollama host must be an absolute HTTP or HTTPS URL.");
        }
    }

    private async Task<string> CallOpenAiAsync(IEnumerable<ChatMessage> messages, LlmOptions options)
    {
        ValidateOpenAiOptions(options);

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
        });
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async Task<string> CallGeminiAsync(IEnumerable<ChatMessage> messages, LlmOptions options)
    {
        ValidateGeminiOptions(options);

        var url = $"https://generativelanguage.googleapis.com/v1/models/{options.Model}:generateContent?key={options.ApiKey}";

        for (var attempt = 1; attempt <= options.MaxRetries; attempt++)
        {
            try
            {
                // Gemini expects a role on each message; omit it and the API may return
                // an empty candidate without parts, which manifests as "response missing text".
                var res = await _http.PostAsJsonAsync(url, new
                {
                    contents = messages.Select(m => new
                    {
                        role = m.Role == "assistant" ? "model" : m.Role,
                        parts = new[] { new { text = m.Content } }
                    })
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
            catch (HttpRequestException ex) when (attempt < options.MaxRetries)
            {
                var delay = ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? TimeSpan.FromSeconds(Math.Pow(2, attempt))
                    : TimeSpan.FromMilliseconds(500 * attempt);

                _logger.LogWarning(
                    ex,
                    "Connection to Gemini API not stable, retrying ({Attempt}/{Max})",
                    attempt,
                    options.MaxRetries);

                await Task.Delay(delay);
            }
            catch (Exception ex) when (attempt < options.MaxRetries)
            {
                _logger.LogWarning(
                    ex,
                    "Connection to Gemini API not stable, retrying ({Attempt}/{Max})",
                    attempt,
                    options.MaxRetries);

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
            }
        }

        throw new InvalidOperationException(
            $"Connection to Gemini API failed after {options.MaxRetries} attempts. Please check connection to API server.");
    }

    private async Task<string> CallOllamaAsync(IEnumerable<ChatMessage> messages, LlmOptions options)
    {
        ValidateOllamaOptions(options);

        var baseUri = new Uri(options.OllamaHost, UriKind.Absolute);
        var url = new Uri(baseUri, "/api/chat");

        var res = await _http.PostAsJsonAsync(url, new
        {
            model = options.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false
        });

        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Ollama API returned {StatusCode}: {Body}",
                (int)res.StatusCode,
                body);
            res.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("response", out var response))
        {
            return response.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Ollama response missing text.");
    }
}
