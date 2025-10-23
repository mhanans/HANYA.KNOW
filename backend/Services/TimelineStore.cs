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

public class TimelineStore
{
    private readonly string _connectionString;
    private readonly ILogger<TimelineStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TimelineStore(IOptions<PostgresOptions> dbOptions, ILogger<TimelineStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task SaveAsync(TimelineRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        const string sql = @"INSERT INTO assessment_timelines (assessment_id, project_name, template_name, generated_at, timeline_data)
                             VALUES (@id, @project, @template, @generated, CAST(@data AS JSONB))
                             ON CONFLICT (assessment_id)
                             DO UPDATE SET project_name = EXCLUDED.project_name,
                                           template_name = EXCLUDED.template_name,
                                           generated_at = EXCLUDED.generated_at,
                                           timeline_data = EXCLUDED.timeline_data";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", record.AssessmentId);
        cmd.Parameters.AddWithValue("project", record.ProjectName);
        cmd.Parameters.AddWithValue("template", record.TemplateName);
        cmd.Parameters.AddWithValue("generated", record.GeneratedAt);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Text, json);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimelineRecord?> GetAsync(int assessmentId, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT timeline_data FROM assessment_timelines WHERE assessment_id=@id";
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
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TimelineRecord>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize stored timeline for assessment {AssessmentId}.", assessmentId);
            return null;
        }
    }

    public async Task<Dictionary<int, TimelineAssessmentSummary>> ListSummariesAsync(CancellationToken cancellationToken)
    {
        const string sql = @"SELECT assessment_id, project_name, template_name, generated_at FROM assessment_timelines";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new Dictionary<int, TimelineAssessmentSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(0);
            var summary = new TimelineAssessmentSummary
            {
                AssessmentId = id,
                ProjectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                TemplateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                HasTimeline = true,
                TimelineGeneratedAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3)
            };
            results[id] = summary;
        }

        return results;
    }
}
