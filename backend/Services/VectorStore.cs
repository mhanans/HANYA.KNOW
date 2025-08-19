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

    public VectorStore(IOptions<PostgresOptions> dbOptions, EmbeddingClient embedding, ILogger<VectorStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _embedding = embedding;
        _logger = logger;
    }

    public async Task IngestAsync(string title, string text)
    {
        _logger.LogInformation("Ingesting document {Title} with {Length} characters", title, text.Length);
        var chunks = Chunk(text, 500);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        foreach (var chunk in chunks)
        {
            var embedding = await _embedding.EmbedAsync(chunk);
            if (embedding == null || embedding.Length == 0)
            {
                _logger.LogWarning("Embedding returned null/empty vector for chunk of {Title}", title);
                throw new InvalidOperationException("Embedding service returned null or empty vector.");
            }

            var sql = "INSERT INTO documents(title, content, embedding) VALUES (@title, @content, @embedding)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("title", title);
            cmd.Parameters.AddWithValue("content", chunk);
            cmd.Parameters.AddWithValue("embedding", embedding);
            cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
            await cmd.ExecuteNonQueryAsync();
            _logger.LogDebug("Stored chunk for {Title} with vector size {Size}", title, embedding.Length);
        }
    }

    public async Task<List<(string Title, string Content, double Score)>> SearchAsync(string query, int topK)
    {
        _logger.LogInformation("Searching for '{Query}' with topK {TopK}", query, topK);
        var embedding = await _embedding.EmbedAsync(query);
        if (embedding == null || embedding.Length == 0)
        {
            _logger.LogWarning("Embedding returned null/empty vector for search query");
            throw new InvalidOperationException("Embedding service returned null or empty vector.");
        }
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var sql = @"SELECT title, content,
                0.5 * (1 - (embedding <=> @embedding)) +
                0.5 * ts_rank_cd(content_tsv, plainto_tsquery('simple', @q)) AS score
            FROM documents
            ORDER BY score DESC
            LIMIT @k";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        cmd.Parameters.AddWithValue("q", query);
        cmd.Parameters.AddWithValue("k", topK);
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string, string, double)>();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
        }
        _logger.LogInformation("Search returned {Count} results", results.Count);
        return results;
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
