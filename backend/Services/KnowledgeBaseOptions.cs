namespace backend.Services;

public class KnowledgeBaseOptions
{
    public string StoragePath { get; set; } = "knowledge-base";
    public int ChunkMaxTokens { get; set; } = 900;
    public int ChunkOverlapTokens { get; set; } = 120;
}
