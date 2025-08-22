using Npgsql;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class StatsStore
{
    private readonly string _connectionString;

    public StatsStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    public async Task LogChatAsync(string question)
    {
        const string sql = "INSERT INTO chats(question) VALUES (@q)";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("q", question);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<DashboardStats> GetStatsAsync()
    {
        const string sql = @"SELECT
            (SELECT COUNT(*) FROM chats) AS chats,
            (SELECT COUNT(*) FROM documents) AS documents,
            (SELECT COUNT(*) FROM categories) AS categories,
            (SELECT COUNT(*) FROM users) AS users";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var chats = reader.GetInt64(0);
        var documents = reader.GetInt64(1);
        var categories = reader.GetInt64(2);
        var users = reader.GetInt64(3);
        return new DashboardStats(chats, documents, categories, users);
    }
}

public record DashboardStats(long Chats, long Documents, long Categories, long Users);

