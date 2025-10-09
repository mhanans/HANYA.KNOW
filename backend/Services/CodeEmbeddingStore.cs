using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class CodeEmbeddingStore
{
    private readonly string _connectionString;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<CodeEmbeddingStore> _logger;
    private readonly int _expectedDimensions;

    public CodeEmbeddingStore(
        IOptions<PostgresOptions> dbOptions,
        IOptions<EmbeddingOptions> embeddingOptions,
        EmbeddingClient embeddingClient,
        ILogger<CodeEmbeddingStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _expectedDimensions = embeddingOptions.Value.Dimensions;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CodeEmbeddingMatch>> SearchAsync(string question, int topK)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required", nameof(question));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "TopK must be positive");

        var embedding = await _embeddingClient.EmbedAsync(question);
        if (embedding == null || embedding.Length == 0)
            throw new InvalidOperationException("Embedding service returned an empty vector.");
        if (embedding.Length != _expectedDimensions)
            throw new InvalidOperationException($"Embedding dimension mismatch: expected {_expectedDimensions} but got {embedding.Length}.");

        const string sql = @"SELECT id, file_path, symbol_name, content, start_line, end_line, embedding <=> @embedding::vector AS distance
            FROM code_embeddings
            ORDER BY embedding <=> @embedding::vector
            LIMIT @k";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        cmd.Parameters.AddWithValue("k", topK);

        var results = new List<CodeEmbeddingMatch>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            var filePath = reader.GetString(1);
            var symbol = reader.IsDBNull(2) ? null : reader.GetString(2);
            var content = reader.GetString(3);
            var start = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var end = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var distance = reader.GetDouble(6);
            var score = 1.0 / (1.0 + distance);
            results.Add(new CodeEmbeddingMatch(id, filePath, symbol, content, start, end, score, distance));
        }

        _logger.LogInformation("Source code search returned {Count} results for '{Question}'", results.Count, question);
        return results;
    }
}

public record CodeEmbeddingMatch(
    Guid Id,
    string FilePath,
    string? SymbolName,
    string Content,
    int? StartLine,
    int? EndLine,
    double Score,
    double Distance);
