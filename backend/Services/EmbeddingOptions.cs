namespace backend.Services;

public class EmbeddingOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = "ollama";
    public int Dimensions { get; set; } = 768;
}
