using System;
using System.Collections.Generic;
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

    public async Task<List<TimelineEstimationReference>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = @"SELECT id, phase_name, input_man_hours, input_resource_count, output_duration_days
                             FROM timeline_estimation_references
                             ORDER BY phase_name, input_man_hours";
        var list = new List<TimelineEstimationReference>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new TimelineEstimationReference
            {
                Id = reader.GetInt32(0),
                PhaseName = reader.GetString(1),
                InputManHours = reader.GetInt32(2),
                InputResourceCount = reader.GetInt32(3),
                OutputDurationDays = reader.GetInt32(4)
            });
        }

        return list;
    }

    public async Task<TimelineEstimationReference?> GetAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT id, phase_name, input_man_hours, input_resource_count, output_duration_days
                             FROM timeline_estimation_references
                             WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new TimelineEstimationReference
            {
                Id = reader.GetInt32(0),
                PhaseName = reader.GetString(1),
                InputManHours = reader.GetInt32(2),
                InputResourceCount = reader.GetInt32(3),
                OutputDurationDays = reader.GetInt32(4)
            };
        }

        return null;
    }

    public async Task<int> CreateAsync(TimelineEstimationReference reference, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO timeline_estimation_references
                             (phase_name, input_man_hours, input_resource_count, output_duration_days)
                             VALUES (@phase, @hours, @resources, @duration)
                             RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("phase", reference.PhaseName);
        cmd.Parameters.AddWithValue("hours", reference.InputManHours);
        cmd.Parameters.AddWithValue("resources", reference.InputResourceCount);
        cmd.Parameters.AddWithValue("duration", reference.OutputDurationDays);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(TimelineEstimationReference reference, CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE timeline_estimation_references
                             SET phase_name=@phase,
                                 input_man_hours=@hours,
                                 input_resource_count=@resources,
                                 output_duration_days=@duration
                             WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("phase", reference.PhaseName);
        cmd.Parameters.AddWithValue("hours", reference.InputManHours);
        cmd.Parameters.AddWithValue("resources", reference.InputResourceCount);
        cmd.Parameters.AddWithValue("duration", reference.OutputDurationDays);
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
}

public class TimelineEstimationReference
{
    public int Id { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public int InputManHours { get; set; }
    public int InputResourceCount { get; set; }
    public int OutputDurationDays { get; set; }
}
