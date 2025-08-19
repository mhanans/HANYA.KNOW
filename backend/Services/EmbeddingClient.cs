using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class EmbeddingClient
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<EmbeddingClient> _logger;

    public EmbeddingClient(IOptions<EmbeddingOptions> options, ILogger<EmbeddingClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri(_options.BaseUrl) };
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        try
        {
            _logger.LogInformation("Requesting embedding of {Length} characters", text.Length);

            var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (_options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new { model = _options.Model, prompt = text };
                _logger.LogDebug("Ollama embed payload: {Payload}", payload);
                var response = await _http.PostAsJsonAsync("/api/embeddings", payload);
                response.EnsureSuccessStatusCode();
                var jsonBody = await response.Content.ReadAsStringAsync();
                var body = JsonSerializer.Deserialize<OllamaEmbedResponse>(jsonBody, jsonOpts);
                var vec = body?.embedding;
                if (vec == null || vec.Length == 0)
                {
                    var responseSnippet = jsonBody.Length > 200 ? jsonBody.Substring(0, 200) + "..." : jsonBody;
                    _logger.LogWarning("Ollama returned empty embedding: {Snippet}", responseSnippet);
                    throw new InvalidOperationException("Embedding service returned empty vector.");
                }
                _logger.LogInformation("Received embedding with {Dim} dimensions", vec.Length);
                return vec;
            }

            var genericPayload = new { model = _options.Model, input = text };
            _logger.LogDebug("Generic embed payload: {Payload}", genericPayload);
            var generic = await _http.PostAsJsonAsync("/embed", genericPayload);
            generic.EnsureSuccessStatusCode();

            var json = await generic.Content.ReadAsStringAsync();

            // try plain float[]
            var array = JsonSerializer.Deserialize<float[]>(json, jsonOpts);
            if (array != null && array.Length > 0)
            {
                _logger.LogInformation("Received embedding with {Dim} dimensions", array.Length);
                return array;
            }

            // try { "embedding": [...] }
            var wrapper = JsonSerializer.Deserialize<EmbedWrapper>(json, jsonOpts);
            if (wrapper?.embedding != null && wrapper.embedding.Length > 0)
            {
                _logger.LogInformation("Received embedding with {Dim} dimensions", wrapper.embedding.Length);
                return wrapper.embedding;
            }

            // try OpenAI style { "data": [ { "embedding": [...] } ] }
            var openAi = JsonSerializer.Deserialize<OpenAiResponse>(json, jsonOpts);
            var first = openAi?.data?.FirstOrDefault();
            if (first?.embedding != null && first.embedding.Length > 0)
            {
                _logger.LogInformation("Received embedding with {Dim} dimensions", first.embedding.Length);
                return first.embedding;
            }

            // fallback: scan JSON for first numeric array
            using var doc = JsonDocument.Parse(json);
            var fallback = FindVector(doc.RootElement);
            if (fallback != null && fallback.Length > 0)
            {
                _logger.LogInformation("Received embedding with {Dim} dimensions", fallback.Length);
                return fallback;
            }

            var payloadSnippet = json.Length > 200 ? json.Substring(0, 200) + "..." : json;
            _logger.LogWarning("Embedding service returned empty or unrecognized payload: {Snippet}", payloadSnippet);
            throw new InvalidOperationException($"Embedding service returned empty or unrecognized payload: {payloadSnippet}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to embedding service");
            throw new InvalidOperationException($"Failed to connect to embedding service: {ex.Message}", ex);
        }
    }

    public async Task TestConnectionAsync()
    {
        try
        {
            if (_options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Testing connection to Ollama at {Url}", _http.BaseAddress);
                var resp = await _http.GetAsync("/api/tags");
                resp.EnsureSuccessStatusCode();
                _logger.LogInformation("Ollama connection OK");
            }
            else
            {
                _logger.LogInformation("Testing connection to embedding service at {Url}", _http.BaseAddress);
                var resp = await _http.GetAsync("/");
                resp.EnsureSuccessStatusCode();
                _logger.LogInformation("Embedding service connection OK");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding service health check failed");
            throw new InvalidOperationException("Embedding service unreachable: " + ex.Message, ex);
        }
    }

    private record OllamaEmbedResponse(float[] embedding);
    private record EmbedWrapper(float[] embedding);
    private record OpenAiResponse(DataItem[] data);
    private record DataItem(float[] embedding);

    private static float[]? FindVector(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var numbers = element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetSingle())
                .ToArray();
            return numbers.Length == element.GetArrayLength() ? numbers : null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var found = FindVector(prop.Value);
                if (found != null)
                    return found;
            }
        }

        return null;
    }
}
