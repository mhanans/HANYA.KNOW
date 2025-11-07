using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Services;

public class TimelineEstimationReferenceStore
{
    private readonly string _connectionString;

    public TimelineEstimationReferenceStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<TimelineEstimationReference>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = @"SELECT id, project_scale, phase_durations, total_duration_days, resource_allocation
                             FROM timeline_estimation_references
                             ORDER BY project_scale, id";
        var list = new List<TimelineEstimationReference>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadReference(reader));
        }

        return list;
    }

    public async Task<TimelineEstimationReference?> GetAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT id, project_scale, phase_durations, total_duration_days, resource_allocation
                             FROM timeline_estimation_references
                             WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadReference(reader);
        }

        return null;
    }

    public async Task<int> CreateAsync(TimelineEstimationReference reference, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO timeline_estimation_references
                             (project_scale, phase_durations, total_duration_days, resource_allocation)
                             VALUES (@scale, CAST(@phases AS JSONB), @total, CAST(@resources AS JSONB))
                             RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("scale", reference.ProjectScale);
        cmd.Parameters.AddWithValue("phases", JsonSerializer.Serialize(reference.PhaseDurations, JsonOptions));
        cmd.Parameters.AddWithValue("total", reference.TotalDurationDays);
        cmd.Parameters.AddWithValue("resources", JsonSerializer.Serialize(reference.ResourceAllocation, JsonOptions));
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(TimelineEstimationReference reference, CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE timeline_estimation_references
                             SET project_scale=@scale,
                                 phase_durations=CAST(@phases AS JSONB),
                                 total_duration_days=@total,
                                 resource_allocation=CAST(@resources AS JSONB)
                             WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("scale", reference.ProjectScale);
        cmd.Parameters.AddWithValue("phases", JsonSerializer.Serialize(reference.PhaseDurations, JsonOptions));
        cmd.Parameters.AddWithValue("total", reference.TotalDurationDays);
        cmd.Parameters.AddWithValue("resources", JsonSerializer.Serialize(reference.ResourceAllocation, JsonOptions));
        cmd.Parameters.AddWithValue("id", reference.Id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
        {
            throw new KeyNotFoundException();
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM timeline_estimation_references WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
        {
            throw new KeyNotFoundException();
        }
    }

    private TimelineEstimationReference ReadReference(NpgsqlDataReader reader)
    {
        var phaseJson = reader.IsDBNull(2) ? "{}" : reader.GetString(2);
        var resourceJson = reader.IsDBNull(4) ? "{}" : reader.GetString(4);
        var phases = JsonSerializer.Deserialize<Dictionary<string, int>>(phaseJson, JsonOptions)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resources = JsonSerializer.Deserialize<Dictionary<string, double>>(resourceJson, JsonOptions)
            ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        return new TimelineEstimationReference
        {
            Id = reader.GetInt32(0),
            ProjectScale = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            PhaseDurations = new Dictionary<string, int>(phases, StringComparer.OrdinalIgnoreCase),
            TotalDurationDays = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            ResourceAllocation = new Dictionary<string, double>(resources, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public class TimelineEstimationReference
{
    public int Id { get; set; }
    public string ProjectScale { get; set; } = string.Empty;
    public Dictionary<string, int> PhaseDurations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalDurationDays { get; set; }
    public Dictionary<string, double> ResourceAllocation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
