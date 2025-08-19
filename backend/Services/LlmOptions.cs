namespace backend.Services;

public class LlmOptions
{
    public string Provider { get; set; } = "openai"; // or gemini
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
