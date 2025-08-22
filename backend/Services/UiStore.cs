using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class UiStore
{
    private readonly string _connectionString;
    private readonly ILogger<UiStore> _logger;

    public UiStore(IOptions<PostgresOptions> dbOptions, ILogger<UiStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<UiPage>> ListAsync()
    {
        const string sql = "SELECT id, key FROM ui_pages ORDER BY key";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UiPage>();
        while (await reader.ReadAsync())
            list.Add(new UiPage { Id = reader.GetInt32(0), Key = reader.GetString(1) });
        return list;
    }
}

public class UiPage
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
}

