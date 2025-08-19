using System.Net.Http.Json;
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
        return (await generic.Content.ReadFromJsonAsync<float[]>())!;
    }

    private record OllamaEmbedResponse(float[] embedding);
}
