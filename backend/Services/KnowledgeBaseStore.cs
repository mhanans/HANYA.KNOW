using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class KnowledgeBaseStore
{
    private readonly string _connectionString;
    private readonly ILogger<KnowledgeBaseStore> _logger;
    private readonly int _expectedDimensions;

    public KnowledgeBaseStore(
        IOptions<PostgresOptions> dbOptions,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<KnowledgeBaseStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _expectedDimensions = embeddingOptions.Value.Dimensions;
        _logger = logger;
    }

    public async Task<int> CreateDocumentAsync(KnowledgeBaseDocument document, CancellationToken cancellationToken = default)
    {
        const string sql = @"INSERT INTO knowledge_base_documents (
                original_file_name,
                storage_path,
                project_name,
                document_type,
                client_type,
                project_completion_date,
                processing_status,
                error_message,
                chunk_count,
                uploaded_by_user_id,
                uploaded_at,
                updated_at)
            VALUES (@name, @path, @project, @doctype, @clientType, @completion, @status, @error, 0, @user, NOW(), NOW())
            RETURNING id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", document.OriginalFileName);
        cmd.Parameters.AddWithValue("path", document.StoragePath);
        cmd.Parameters.AddWithValue("project", (object?)document.ProjectName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("doctype", (object?)document.DocumentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("clientType", (object?)document.ClientType ?? DBNull.Value);
        if (document.ProjectCompletionDate.HasValue)
        {
            cmd.Parameters.AddWithValue("completion", document.ProjectCompletionDate.Value);
        }
        else
        {
            cmd.Parameters.AddWithValue("completion", DBNull.Value);
        }
        cmd.Parameters.AddWithValue("status", document.ProcessingStatus);
        cmd.Parameters.AddWithValue("error", (object?)document.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("user", (object?)document.UploadedByUserId ?? DBNull.Value);
        var id = await cmd.ExecuteScalarAsync(cancellationToken);
        var documentId = Convert.ToInt32(id);
        _logger.LogInformation("Queued knowledge base document {DocumentId} ({FileName}) with status {Status}", documentId, document.OriginalFileName, document.ProcessingStatus);
        return documentId;
    }

    public async Task<KnowledgeBaseDocument?> GetDocumentAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT id, original_file_name, storage_path, project_name, document_type, client_type,
                project_completion_date, processing_status, error_message, chunk_count, uploaded_by_user_id, uploaded_at,
                processed_at, updated_at
            FROM knowledge_base_documents
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return MapDocument(reader);
    }

    public async Task<List<KnowledgeBaseDocument>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT id, original_file_name, storage_path, project_name, document_type, client_type,
                project_completion_date, processing_status, error_message, chunk_count, uploaded_by_user_id, uploaded_at,
                processed_at, updated_at
            FROM knowledge_base_documents
            ORDER BY uploaded_at DESC, id DESC";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var documents = new List<KnowledgeBaseDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(MapDocument(reader));
        }
        return documents;
    }

    public async Task MarkProcessingAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = @"UPDATE knowledge_base_documents
            SET processing_status = @status,
                error_message = NULL,
                processed_at = NULL,
                chunk_count = NULL,
                updated_at = NOW()
            WHERE id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", KnowledgeBaseDocumentStatus.Processing);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Document {DocumentId} marked as processing", id);
    }

    public async Task MarkFailedAsync(int id, string errorMessage, CancellationToken cancellationToken = default)
    {
        if (errorMessage.Length > 2000)
        {
            errorMessage = errorMessage[..2000];
        }
        const string sql = @"UPDATE knowledge_base_documents
            SET processing_status = @status,
                error_message = @error,
                updated_at = NOW()
            WHERE id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", KnowledgeBaseDocumentStatus.Failed);
        cmd.Parameters.AddWithValue("error", errorMessage);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogWarning("Document {DocumentId} processing failed: {Error}", id, errorMessage);
    }

    public async Task MarkIndexedAsync(int id, int chunkCount, CancellationToken cancellationToken = default)
    {
        const string sql = @"UPDATE knowledge_base_documents
            SET processing_status = @status,
                error_message = NULL,
                processed_at = NOW(),
                updated_at = NOW(),
                chunk_count = @chunks
            WHERE id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("status", KnowledgeBaseDocumentStatus.Indexed);
        cmd.Parameters.AddWithValue("chunks", chunkCount);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Document {DocumentId} marked as indexed with {ChunkCount} chunks", id, chunkCount);
    }

    public async Task DeleteDocumentAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM knowledge_base_documents WHERE id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Deleted knowledge base document {DocumentId}", id);
    }

    public async Task DeleteChunksAsync(int documentId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM knowledge_base_chunks WHERE document_id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", documentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Cleared existing chunks for document {DocumentId}", documentId);
    }

    public async Task InsertChunkAsync(
        int documentId,
        int chunkIndex,
        int? pageNumber,
        string content,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        if (embedding.Length != _expectedDimensions)
        {
            throw new InvalidOperationException($"Embedding dimension mismatch. Expected {_expectedDimensions} but received {embedding.Length}.");
        }

        const string sql = @"INSERT INTO knowledge_base_chunks (document_id, chunk_index, page_number, content, embedding)
            VALUES (@doc, @idx, @page, @content, @embedding::vector)";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("doc", documentId);
        cmd.Parameters.AddWithValue("idx", chunkIndex);
        if (pageNumber.HasValue)
            cmd.Parameters.AddWithValue("page", pageNumber.Value);
        else
            cmd.Parameters.AddWithValue("page", DBNull.Value);
        cmd.Parameters.AddWithValue("content", content);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static KnowledgeBaseDocument MapDocument(NpgsqlDataReader reader)
    {
        return new KnowledgeBaseDocument
        {
            Id = reader.GetInt32(0),
            OriginalFileName = reader.GetString(1),
            StoragePath = reader.GetString(2),
            ProjectName = reader.IsDBNull(3) ? null : reader.GetString(3),
            DocumentType = reader.IsDBNull(4) ? null : reader.GetString(4),
            ClientType = reader.IsDBNull(5) ? null : reader.GetString(5),
            ProjectCompletionDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            ProcessingStatus = reader.GetString(7),
            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            ChunkCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            UploadedByUserId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            UploadedAt = reader.GetDateTime(11),
            ProcessedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            UpdatedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
        };
    }
}
