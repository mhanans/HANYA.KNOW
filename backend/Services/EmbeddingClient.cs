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
        var response = await _http.PostAsJsonAsync("/embed", new { model = _options.Model, input = text });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<float[]>())!;
    }
}
