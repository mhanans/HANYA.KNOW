using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Services;

public class CostEstimationConfigurationStore
{
    private const string SettingsKey = "CostEstimationConfiguration";
    private readonly string _connectionString;
    private readonly ILogger<CostEstimationConfigurationStore> _logger;

    public CostEstimationConfigurationStore(IOptions<PostgresOptions> options, ILogger<CostEstimationConfigurationStore> logger)
    {
        _connectionString = options.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<CostEstimationConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        var config = await TryReadAsync(cancellationToken).ConfigureAwait(false);
        return config ?? CreateDefaultConfiguration();
    }

    public async Task<CostEstimationConfiguration> SaveAsync(CostEstimationConfiguration configuration, CancellationToken cancellationToken)
    {
        configuration ??= CreateDefaultConfiguration();
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        const string sql = "INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", SettingsKey);
        cmd.Parameters.AddWithValue("value", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return configuration;
    }

    private async Task<CostEstimationConfiguration?> TryReadAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT value FROM settings WHERE key=@key";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", SettingsKey);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var json = reader.IsDBNull(0) ? null : reader.GetString(0);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CostEstimationConfiguration>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize cost estimation configuration stored in settings table.");
            return null;
        }
    }

    private static CostEstimationConfiguration CreateDefaultConfiguration()
    {
        var config = new CostEstimationConfiguration
        {
            DefaultRateCardKey = "default",
            DefaultWorstCaseBufferPercent = 30m,
            DefaultAnnualInterestRatePercent = 30m,
            DefaultClientPaymentDelayMonths = 1m,
            DefaultOverheadPercent = 30m,
            DefaultOperationalCostPercent = 10m,
            DefaultPphPercent = 1m,
            DefaultExternalCommissionPercent = 0m,
            DefaultExternalCommissionMode = CommissionMode.Percentage,
            DefaultMultiplier = 1m,
            DefaultDiscountPercent = 0m,
        };

        config.RoleMonthlySalaries["PM"] = 45000000m;
        config.RoleMonthlySalaries["BA Junior"] = 12000000m;
        config.RoleMonthlySalaries["BA Senior"] = 22000000m;
        config.RoleMonthlySalaries["Dev Junior"] = 15000000m;
        config.RoleMonthlySalaries["Dev Senior"] = 30000000m;
        config.RoleMonthlySalaries["QA"] = 18000000m;
        config.RoleMonthlySalaries["UI/UX"] = 20000000m;

        config.RoleDefaultHeadcount["PM"] = 0.5m;

        config.RateCards["default"] = new RateCardDefinition
        {
            DisplayName = "Default",
            RoleRates =
            {
                ["PM"] = 3500000m,
                ["BA Junior"] = 1800000m,
                ["Dev Junior"] = 2000000m,
                ["Dev Senior"] = 2800000m,
                ["QA"] = 1900000m,
                ["UI/UX"] = 2100000m,
            }
        };

        config.SalesCommissionBrackets.Add(new CommissionBracket { UpperBound = 200_000_000m, RatePercent = 3m });
        config.SalesCommissionBrackets.Add(new CommissionBracket { UpperBound = 1_000_000_000m, RatePercent = 1m });
        config.SalesCommissionBrackets.Add(new CommissionBracket { UpperBound = 0m, RatePercent = 0.25m });

        config.CostCommissionBrackets.Add(new CommissionBracket { UpperBound = 200_000_000m, RatePercent = 3m });
        config.CostCommissionBrackets.Add(new CommissionBracket { UpperBound = 1_000_000_000m, RatePercent = 2m });
        config.CostCommissionBrackets.Add(new CommissionBracket { UpperBound = 0m, RatePercent = 1m });

        return config;
    }
}
