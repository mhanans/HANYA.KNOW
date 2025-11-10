using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using backend.Configuration;
using backend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class PresalesConfigurationStore
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PresalesConfigurationStore> _logger;

    public PresalesConfigurationStore(
        IOptions<PostgresOptions> dbOptions,
        ILogger<PresalesConfigurationStore> logger,
        IConfiguration configuration)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
        _configuration = configuration;
    }

    public EffectiveEstimationPolicy GetEffectiveEstimationPolicy()
    {
        var policy = new EffectiveEstimationPolicy();
        try
        {
            var section = _configuration.GetSection("EffectiveEstimationPolicy");
            if (section.Exists())
            {
                section.Bind(policy);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bind EffectiveEstimationPolicy from configuration. Using defaults.");
        }

        return policy;
    }

    public async Task<PresalesConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var configuration = new PresalesConfiguration();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string roleSql = "SELECT role_name, expected_level, cost_per_day FROM presales_roles ORDER BY role_name";
        await using (var roleCmd = new NpgsqlCommand(roleSql, conn))
        await using (var reader = await roleCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var role = new PresalesRole
                {
                    RoleName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ExpectedLevel = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    CostPerDay = reader.IsDBNull(2) ? 0 : reader.GetFieldValue<decimal>(2)
                };
                if (!string.IsNullOrWhiteSpace(role.RoleName))
                {
                    configuration.Roles.Add(role);
                }
            }
        }

        const string activitySql = "SELECT activity_name, display_order FROM presales_activities ORDER BY display_order, activity_name";
        await using (var activityCmd = new NpgsqlCommand(activitySql, conn))
        await using (var reader = await activityCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var activity = new PresalesActivity
                {
                    ActivityName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    DisplayOrder = reader.IsDBNull(1) ? 1 : reader.GetInt32(1)
                };
                if (!string.IsNullOrWhiteSpace(activity.ActivityName))
                {
                    configuration.Activities.Add(activity);
                }
            }
        }

        const string itemActivitySql = "SELECT item_name, activity_name FROM presales_item_activities ORDER BY item_name";
        await using (var itemActivityCmd = new NpgsqlCommand(itemActivitySql, conn))
        await using (var reader = await itemActivityCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapping = new ItemActivityMapping
                {
                    ItemName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ActivityName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                };
                if (!string.IsNullOrWhiteSpace(mapping.ItemName))
                {
                    configuration.ItemActivities.Add(mapping);
                }
            }
        }

        const string columnRoleSql = "SELECT estimation_column, role_name FROM presales_estimation_column_roles ORDER BY estimation_column, role_name";
        await using (var columnRoleCmd = new NpgsqlCommand(columnRoleSql, conn))
        await using (var reader = await columnRoleCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapping = new EstimationColumnRoleMapping
                {
                    EstimationColumn = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    RoleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                };
                if (!string.IsNullOrWhiteSpace(mapping.EstimationColumn) && !string.IsNullOrWhiteSpace(mapping.RoleName))
                {
                    configuration.EstimationColumnRoles.Add(mapping);
                }
            }
        }

        return configuration;
    }

    public async Task<PresalesConfiguration> SaveConfigurationAsync(PresalesConfiguration configuration, CancellationToken cancellationToken)
    {
        configuration ??= new PresalesConfiguration();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using (var clearColumnRoles = new NpgsqlCommand("DELETE FROM presales_estimation_column_roles", conn, tx))
            {
                await clearColumnRoles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var clearItemActivities = new NpgsqlCommand("DELETE FROM presales_item_activities", conn, tx))
            {
                await clearItemActivities.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var clearRoles = new NpgsqlCommand("DELETE FROM presales_roles", conn, tx))
            {
                await clearRoles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var clearActivities = new NpgsqlCommand("DELETE FROM presales_activities", conn, tx))
            {
                await clearActivities.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertRoleSql = "INSERT INTO presales_roles (role_name, expected_level, cost_per_day) VALUES (@name, @level, @cost)";
            foreach (var role in configuration.Roles)
            {
                if (string.IsNullOrWhiteSpace(role?.RoleName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertRoleSql, conn, tx);
                cmd.Parameters.AddWithValue("name", role.RoleName.Trim());
                cmd.Parameters.AddWithValue("level", role.ExpectedLevel?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("cost", NpgsqlDbType.Numeric, role.CostPerDay);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertActivitySql = "INSERT INTO presales_activities (activity_name, display_order) VALUES (@name, @order)";
            foreach (var activity in configuration.Activities)
            {
                if (string.IsNullOrWhiteSpace(activity?.ActivityName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertActivitySql, conn, tx);
                cmd.Parameters.AddWithValue("name", activity.ActivityName.Trim());
                cmd.Parameters.AddWithValue("order", activity.DisplayOrder);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertItemActivitySql = "INSERT INTO presales_item_activities (item_name, activity_name) VALUES (@item, @activity)";
            foreach (var mapping in configuration.ItemActivities)
            {
                if (string.IsNullOrWhiteSpace(mapping?.ItemName) || string.IsNullOrWhiteSpace(mapping.ActivityName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertItemActivitySql, conn, tx);
                cmd.Parameters.AddWithValue("item", mapping.ItemName.Trim());
                cmd.Parameters.AddWithValue("activity", mapping.ActivityName.Trim());
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertColumnRoleSql = "INSERT INTO presales_estimation_column_roles (estimation_column, role_name) VALUES (@column, @role)";
            foreach (var mapping in configuration.EstimationColumnRoles)
            {
                if (string.IsNullOrWhiteSpace(mapping?.EstimationColumn) || string.IsNullOrWhiteSpace(mapping.RoleName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertColumnRoleSql, conn, tx);
                cmd.Parameters.AddWithValue("column", mapping.EstimationColumn.Trim());
                cmd.Parameters.AddWithValue("role", mapping.RoleName.Trim());
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to persist presales configuration.");
            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
