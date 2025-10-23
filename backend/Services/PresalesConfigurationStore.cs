using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class PresalesConfigurationStore
{
    private readonly string _connectionString;
    private readonly ILogger<PresalesConfigurationStore> _logger;

    public PresalesConfigurationStore(IOptions<PostgresOptions> dbOptions, ILogger<PresalesConfigurationStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
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

        const string taskActivitySql = "SELECT task_key, activity_name FROM presales_task_activities ORDER BY task_key";
        await using (var taskActivityCmd = new NpgsqlCommand(taskActivitySql, conn))
        await using (var reader = await taskActivityCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapping = new TaskActivityMapping
                {
                    TaskKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ActivityName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                };
                if (!string.IsNullOrWhiteSpace(mapping.TaskKey))
                {
                    configuration.TaskActivities.Add(mapping);
                }
            }
        }

        const string taskRoleSql = "SELECT task_key, role_name, allocation_percentage FROM presales_task_roles ORDER BY task_key, role_name";
        await using (var taskRoleCmd = new NpgsqlCommand(taskRoleSql, conn))
        await using (var reader = await taskRoleCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var mapping = new TaskRoleMapping
                {
                    TaskKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    RoleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    AllocationPercentage = reader.IsDBNull(2) ? 0 : reader.GetDouble(2)
                };
                if (!string.IsNullOrWhiteSpace(mapping.TaskKey) && !string.IsNullOrWhiteSpace(mapping.RoleName))
                {
                    configuration.TaskRoles.Add(mapping);
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
            await using (var clearTaskRoles = new NpgsqlCommand("DELETE FROM presales_task_roles", conn, tx))
            {
                await clearTaskRoles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var clearTaskActivities = new NpgsqlCommand("DELETE FROM presales_task_activities", conn, tx))
            {
                await clearTaskActivities.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

            const string insertTaskActivitySql = "INSERT INTO presales_task_activities (task_key, activity_name) VALUES (@task, @activity)";
            foreach (var mapping in configuration.TaskActivities)
            {
                if (string.IsNullOrWhiteSpace(mapping?.TaskKey) || string.IsNullOrWhiteSpace(mapping.ActivityName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertTaskActivitySql, conn, tx);
                cmd.Parameters.AddWithValue("task", mapping.TaskKey.Trim());
                cmd.Parameters.AddWithValue("activity", mapping.ActivityName.Trim());
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string insertTaskRoleSql = "INSERT INTO presales_task_roles (task_key, role_name, allocation_percentage) VALUES (@task, @role, @pct)";
            foreach (var mapping in configuration.TaskRoles)
            {
                if (string.IsNullOrWhiteSpace(mapping?.TaskKey) || string.IsNullOrWhiteSpace(mapping.RoleName))
                {
                    continue;
                }

                await using var cmd = new NpgsqlCommand(insertTaskRoleSql, conn, tx);
                cmd.Parameters.AddWithValue("task", mapping.TaskKey.Trim());
                cmd.Parameters.AddWithValue("role", mapping.RoleName.Trim());
                cmd.Parameters.AddWithValue("pct", mapping.AllocationPercentage);
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
