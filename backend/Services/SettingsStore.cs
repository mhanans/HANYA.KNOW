using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class SettingsStore
{
    private readonly string _connectionString;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(IOptions<PostgresOptions> dbOptions, ILogger<SettingsStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<AppSettings> GetAsync()
    {
        const string sql = "SELECT key, value FROM settings WHERE key IN ('ApplicationName','LogoUrl','LlmProvider','LlmModel','LlmApiKey','OllamaHost')";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var settings = new AppSettings();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            if (key == "ApplicationName") settings.ApplicationName = string.IsNullOrWhiteSpace(value) ? null : value;
            if (key == "LogoUrl") settings.LogoUrl = string.IsNullOrWhiteSpace(value) ? null : value;
            if (key == "LlmProvider") settings.LlmProvider = string.IsNullOrWhiteSpace(value) ? null : value;
            if (key == "LlmModel") settings.LlmModel = string.IsNullOrWhiteSpace(value) ? null : value;
            if (key == "LlmApiKey") settings.LlmApiKey = string.IsNullOrWhiteSpace(value) ? null : value;
            if (key == "OllamaHost") settings.OllamaHost = string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return settings;
    }

    public async Task UpdateAsync(AppSettings settings)
    {
        const string sql = "INSERT INTO settings(key,value) VALUES (@k,@v) ON CONFLICT (key) DO UPDATE SET value=EXCLUDED.value";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        foreach (var pair in new Dictionary<string,string?>
        {
            ["ApplicationName"] = settings.ApplicationName,
            ["LogoUrl"] = settings.LogoUrl,
            ["LlmProvider"] = settings.LlmProvider,
            ["LlmModel"] = settings.LlmModel,
            ["LlmApiKey"] = settings.LlmApiKey,
            ["OllamaHost"] = settings.OllamaHost
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("k", pair.Key);
            cmd.Parameters.AddWithValue("v", pair.Value ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public class AppSettings
{
    public string? ApplicationName { get; set; }
    public string? LogoUrl { get; set; }
    public string? LlmProvider { get; set; }
    public string? LlmModel { get; set; }
    public string? LlmApiKey { get; set; }
    public string? OllamaHost { get; set; }
}
