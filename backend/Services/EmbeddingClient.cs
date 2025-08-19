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
        if (_options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            var response = await _http.PostAsJsonAsync("/api/embed", new { model = _options.Model, input = text });
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
            return payload!.embedding;
        }

        var generic = await _http.PostAsJsonAsync("/embed", new { model = _options.Model, input = text });
        generic.EnsureSuccessStatusCode();

        var json = await generic.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // try plain float[]
        var array = JsonSerializer.Deserialize<float[]>(json, opts);
        if (array != null)
            return array;

        // try { "embedding": [...] }
        var wrapper = JsonSerializer.Deserialize<EmbedWrapper>(json, opts);
        if (wrapper?.embedding != null)
            return wrapper.embedding;

        // try OpenAI style { "data": [ { "embedding": [...] } ] }
        var openAi = JsonSerializer.Deserialize<OpenAiResponse>(json, opts);
        var first = openAi?.data?.FirstOrDefault();
        if (first?.embedding != null)
            return first.embedding;

        throw new InvalidOperationException("Embedding service returned empty or unrecognized payload.");
    }

    private record OllamaEmbedResponse(float[] embedding);
    private record EmbedWrapper(float[] embedding);
    private record OpenAiResponse(DataItem[] data);
    private record DataItem(float[] embedding);
}
