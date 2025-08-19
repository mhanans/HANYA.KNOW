using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class EmbeddingClient
{
    private readonly HttpClient _http;
    private readonly EmbeddingOptions _options;

    public EmbeddingClient(IOptions<EmbeddingOptions> options)
    {
        _options = options.Value;
        _http = new HttpClient { BaseAddress = new Uri(_options.BaseUrl) };
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        try
        {
            if (_options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                var response = await _http.PostAsJsonAsync("/api/embed", new { model = _options.Model, input = text });
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
                var vec = payload?.embedding;
                if (vec == null || vec.Length == 0)
                    throw new InvalidOperationException("Embedding service returned empty vector.");
                return vec;
            }

            var generic = await _http.PostAsJsonAsync("/embed", new { model = _options.Model, input = text });
            generic.EnsureSuccessStatusCode();

            var json = await generic.Content.ReadAsStringAsync();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // try plain float[]
            var array = JsonSerializer.Deserialize<float[]>(json, opts);
            if (array != null && array.Length > 0)
                return array;

            // try { "embedding": [...] }
            var wrapper = JsonSerializer.Deserialize<EmbedWrapper>(json, opts);
            if (wrapper?.embedding != null && wrapper.embedding.Length > 0)
                return wrapper.embedding;

            // try OpenAI style { "data": [ { "embedding": [...] } ] }
            var openAi = JsonSerializer.Deserialize<OpenAiResponse>(json, opts);
            var first = openAi?.data?.FirstOrDefault();
            if (first?.embedding != null && first.embedding.Length > 0)
                return first.embedding;

            throw new InvalidOperationException("Embedding service returned empty or unrecognized payload.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to embedding service: {ex.Message}", ex);
        }
    }

    private record OllamaEmbedResponse(float[] embedding);
    private record EmbedWrapper(float[] embedding);
    private record OpenAiResponse(DataItem[] data);
    private record DataItem(float[] embedding);
}
