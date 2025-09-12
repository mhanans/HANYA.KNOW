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

    public async Task<List<UiPage>> ListForRolesAsync(IEnumerable<int> roleIds)
    {
        var ids = roleIds?.ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0) return new List<UiPage>();
        const string sql = @"SELECT DISTINCT u.id, u.key
                             FROM ui_pages u
                             JOIN role_ui ru ON ru.ui_id = u.id
                             WHERE ru.role_id = ANY(@rids)
                             ORDER BY u.key";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("rids", ids);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UiPage>();
        while (await reader.ReadAsync())
            list.Add(new UiPage { Id = reader.GetInt32(0), Key = reader.GetString(1) });
        return list;
    }

    public async Task<bool> HasAccessAsync(IEnumerable<int> roleIds, string key)
    {
        var ids = roleIds?.ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0) return false;
        const string sql = @"SELECT 1 FROM role_ui ru
                             JOIN ui_pages u ON ru.ui_id = u.id
                             WHERE ru.role_id = ANY(@rids) AND u.key=@key
                             LIMIT 1";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("rids", ids);
        cmd.Parameters.AddWithValue("key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }
}

public class UiPage
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
}

