using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class VectorStore
{
    private readonly string _connectionString;
    private readonly EmbeddingClient _embedding;
    private readonly ILogger<VectorStore> _logger;
    private readonly int _expectedDim;

    public VectorStore(IOptions<PostgresOptions> dbOptions, IOptions<EmbeddingOptions> embedOptions, EmbeddingClient embedding, ILogger<VectorStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _expectedDim = embedOptions.Value.Dimensions;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task IngestAsync(string source, int? page, string text, int? categoryId)
    {
        _logger.LogInformation("Ingesting {Source} page {Page} with {Length} characters in category {Category}", source, page, text.Length, categoryId);
        var embedding = await _embedding.EmbedAsync(text);
        if (embedding == null || embedding.Length == 0)
        {
            _logger.LogWarning("Embedding returned null/empty vector for {Source} page {Page}", source, page);
            throw new InvalidOperationException("Embedding service returned null or empty vector.");
        }
        if (embedding.Length != _expectedDim)
        {
            _logger.LogWarning("Embedding dimension {Actual} does not match expected {Expected} for {Source}", embedding.Length, _expectedDim, source);
            throw new InvalidOperationException($"Embedding dimension mismatch: expected {_expectedDim} but got {embedding.Length}.");
        }

        const string sql = "INSERT INTO documents(source, page, content, embedding, category_id) VALUES (@source, @page, @content, @embedding::vector, @cat)";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("source", source);
        if (page.HasValue)
            cmd.Parameters.AddWithValue("page", page.Value);
        else
            cmd.Parameters.AddWithValue("page", DBNull.Value);
        cmd.Parameters.AddWithValue("content", text);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        if (categoryId.HasValue)
            cmd.Parameters.AddWithValue("cat", categoryId.Value);
        else
            cmd.Parameters.AddWithValue("cat", DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogDebug("Stored {Source} page {Page} with vector size {Size}", source, page, embedding.Length);
    }

    public async Task<List<(string Source, int? Page, string Content, double Score)>> SearchAsync(string query, int topK, int[]? categories = null)
    {
        _logger.LogInformation("Searching for '{Query}' with topK {TopK} in categories {Categories}", query, topK, categories);
        var embedding = await _embedding.EmbedAsync(query);
        if (embedding == null || embedding.Length == 0)
        {
            _logger.LogWarning("Embedding returned null/empty vector for search query");
            throw new InvalidOperationException("Embedding service returned null or empty vector.");
        }
        if (embedding.Length != _expectedDim)
        {
            _logger.LogWarning("Embedding dimension {Actual} does not match expected {Expected} for search query", embedding.Length, _expectedDim);
            throw new InvalidOperationException($"Embedding dimension mismatch: expected {_expectedDim} but got {embedding.Length}.");
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var filter = (categories != null && categories.Length > 0) ? "WHERE category_id = ANY(@cats)" : string.Empty;
        var sql = $@"SELECT source, page, content,
                0.5 * (1 - (embedding <=> @embedding::vector)) +
                0.5 * ts_rank_cd(content_tsv, plainto_tsquery('simple', @q)) AS score
            FROM documents
            {filter}
            ORDER BY score DESC
            LIMIT @k";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        cmd.Parameters.AddWithValue("q", query);
        cmd.Parameters.AddWithValue("k", topK);
        if (categories != null && categories.Length > 0)
        {
            cmd.Parameters.AddWithValue("cats", categories);
            cmd.Parameters["cats"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer;
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string, int?, string, double)>();
        while (await reader.ReadAsync())
        {
            var source = reader.GetString(0);
            int? page = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            var content = reader.GetString(2);
            var score = reader.GetDouble(3);
            results.Add((source, page, content, score));
        }
        _logger.LogInformation("Search returned {Count} results", results.Count);
        return results;
    }

    public async Task<IReadOnlyList<DocumentInfo>> ListDocumentsAsync()
    {
        const string sql = "SELECT source, MIN(category_id) AS category_id, COUNT(*) AS pages FROM documents GROUP BY source ORDER BY source";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var docs = new List<DocumentInfo>();
        while (await reader.ReadAsync())
        {
            var source = reader.GetString(0);
            int? cat = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            var pages = reader.GetInt32(2);
            docs.Add(new DocumentInfo(source, cat, pages));
        }
        return docs;
    }

    public async Task UpdateDocumentCategoryAsync(string source, int? categoryId)
    {
        const string sql = "UPDATE documents SET category_id = @cat WHERE source = @src";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("src", source);
        if (categoryId.HasValue)
            cmd.Parameters.AddWithValue("cat", categoryId.Value);
        else
            cmd.Parameters.AddWithValue("cat", DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteDocumentAsync(string source)
    {
        const string sql = "DELETE FROM documents WHERE source = @src";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("src", source);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetDocumentSummaryAsync(string source)
    {
        const string sql = "SELECT content FROM documents WHERE source = @src ORDER BY page LIMIT 5";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("src", source);
        await using var reader = await cmd.ExecuteReaderAsync();
        var sb = new System.Text.StringBuilder();
        while (await reader.ReadAsync())
        {
            sb.AppendLine(reader.GetString(0));
        }
        var text = sb.ToString().Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > 500 ? text.Substring(0, 500) + "..." : text;
    }
}

public record DocumentInfo(string Source, int? CategoryId, int Pages);
