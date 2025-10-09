using System;

namespace backend.Services;

public class SourceCodeOptions
{
    public int DefaultTopK { get; set; } = 6;
    public double SimilarityThreshold { get; set; } = 0.3;
    public string PromptTemplate { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = "source-code";
    public string[] IncludeExtensions { get; set; } = Array.Empty<string>();
    public string[] ExcludeDirectories { get; set; } = Array.Empty<string>();
    public int ChunkSize { get; set; } = 200;
    public int ChunkOverlap { get; set; } = 40;
}
