using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class VectorStore
{
    private readonly string _connectionString;
    private readonly EmbeddingClient _embedding;

    public VectorStore(IOptions<PostgresOptions> dbOptions, EmbeddingClient embedding)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _embedding = embedding;
    }

    public async Task IngestAsync(string title, string text)
    {
        var chunks = Chunk(text, 500);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        foreach (var chunk in chunks)
        {
            var embedding = await _embedding.EmbedAsync(chunk);
            var sql = "INSERT INTO documents(title, content, embedding) VALUES (@title, @content, @embedding)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("title", title);
            cmd.Parameters.AddWithValue("content", chunk);
            cmd.Parameters.AddWithValue("embedding", embedding);
            cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<(string Title, string Content, double Score)>> SearchAsync(string query, int topK)
    {
        var embedding = await _embedding.EmbedAsync(query);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var sql = @"SELECT title, content,
                0.5 * (1 - (embedding <=> @embedding)) +
                0.5 * ts_rank_cd(content_tsv, plainto_tsquery(@q)) AS score
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
        return results;
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
