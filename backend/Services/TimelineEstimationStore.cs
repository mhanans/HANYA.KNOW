using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class TimelineEstimationStore
{
    private readonly string _connectionString;
    private readonly ILogger<TimelineEstimationStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TimelineEstimationStore(IOptions<PostgresOptions> dbOptions, ILogger<TimelineEstimationStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task SaveAsync(TimelineEstimationRecord record, CancellationToken cancellationToken)
    {
        var rawInput = record.RawInputData;
        record.RawInputData = null;
        var json = JsonSerializer.Serialize(record, JsonOptions);
        record.RawInputData = rawInput;
        var rawInputJson = rawInput == null ? null : JsonSerializer.Serialize(rawInput, JsonOptions);

        const string sql = @"INSERT INTO assessment_timeline_estimations (assessment_id, project_name, template_name, generated_at, estimation_data, raw_input_data)
                             VALUES (@id, @project, @template, @generated, @data, @rawInput)
                             ON CONFLICT (assessment_id)
                             DO UPDATE SET project_name = EXCLUDED.project_name,
                                           template_name = EXCLUDED.template_name,
                                           generated_at = EXCLUDED.generated_at,
                                           estimation_data = EXCLUDED.estimation_data,
                                           raw_input_data = EXCLUDED.raw_input_data";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", record.AssessmentId);
        cmd.Parameters.AddWithValue("project", record.ProjectName);
        cmd.Parameters.AddWithValue("template", record.TemplateName);
        cmd.Parameters.AddWithValue("generated", record.GeneratedAt);
        var estimationParam = cmd.Parameters.Add("data", NpgsqlDbType.Jsonb);
        estimationParam.Value = json;
        var rawInputParam = cmd.Parameters.Add("rawInput", NpgsqlDbType.Jsonb);
        rawInputParam.Value = rawInputJson ?? (object)DBNull.Value;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimelineEstimationRecord?> GetAsync(int assessmentId, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT estimation_data, raw_input_data FROM assessment_timeline_estimations WHERE assessment_id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var json = reader.IsDBNull(0) ? null : reader.GetString(0);
        var rawInputJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<TimelineEstimationRecord>(json, JsonOptions);
            if (record != null && !string.IsNullOrWhiteSpace(rawInputJson))
            {
                try
                {
                    record.RawInputData = JsonSerializer.Deserialize<TimelineEstimatorRawInput>(rawInputJson, JsonOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize stored raw input for timeline estimation {AssessmentId}.", assessmentId);
                }
            }

            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize stored timeline estimation for assessment {AssessmentId}.", assessmentId);
            return null;
        }
    }

    public async Task<Dictionary<int, TimelineEstimationSummarySnapshot>> ListSummariesAsync(
        CancellationToken cancellationToken)
    {
        const string sql = @"SELECT assessment_id, project_name, template_name, generated_at, estimation_data->>'projectScale'
                             FROM assessment_timeline_estimations";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new Dictionary<int, TimelineEstimationSummarySnapshot>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var assessmentId = reader.GetInt32(0);
            var snapshot = new TimelineEstimationSummarySnapshot
            {
                AssessmentId = assessmentId,
                ProjectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                TemplateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                GeneratedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                ProjectScale = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            };
            results[assessmentId] = snapshot;
        }

        return results;
    }
}

public class TimelineEstimationSummarySnapshot
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateTime? GeneratedAt { get; set; }
    public string ProjectScale { get; set; } = string.Empty;
}
