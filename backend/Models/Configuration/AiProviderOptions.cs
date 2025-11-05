using System;
using System.Collections.Generic;

namespace backend.Models.Configuration;

public class AiProviderOptions
{
    public string? DefaultProvider { get; set; }
    public Dictionary<string, AiRouteOptions> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GeminiProviderOptions Gemini { get; set; } = new();
    public OllamaProviderOptions Ollama { get; set; } = new();
    public OpenAiProviderOptions OpenAi { get; set; } = new();
    public MiniMaxProviderOptions MiniMax { get; set; } = new();
}

public class AiRouteOptions
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
}

public class GeminiProviderOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}

public class OllamaProviderOptions
{
    public string? Model { get; set; }
    public string? Host { get; set; }
}

public class OpenAiProviderOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}

public class MiniMaxProviderOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
}
