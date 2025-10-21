using System;

namespace backend.Models;

public class KnowledgeBaseDocument
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? DocumentType { get; set; }
    public string? ClientType { get; set; }
    public DateTime? ProjectCompletionDate { get; set; }
    public string ProcessingStatus { get; set; } = KnowledgeBaseDocumentStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int? ChunkCount { get; set; }
    public int? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class KnowledgeBaseDocumentStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Indexed = "Indexed";
    public const string Failed = "Failed";
}

public class KnowledgeBaseChunk
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public int? PageNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime CreatedAt { get; set; }
}
