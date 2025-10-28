namespace backend.Services;

public class LlmOptions
{
    public string Provider { get; set; } = "openai"; // or gemini
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string OllamaHost { get; set; } = "http://localhost:11434";
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 300;
}
