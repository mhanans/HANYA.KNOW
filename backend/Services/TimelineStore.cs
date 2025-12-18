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
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await EnsureSchemaAsync(conn, cancellationToken);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        const string sql = @"INSERT INTO assessment_timelines (assessment_id, version, project_name, template_name, generated_at, timeline_data)
                             VALUES (@id, @version, @project, @template, @generated, CAST(@data AS JSONB))
                             ON CONFLICT (assessment_id, version)
                             DO UPDATE SET project_name = EXCLUDED.project_name,
                                           template_name = EXCLUDED.template_name,
                                           generated_at = EXCLUDED.generated_at,
                                           timeline_data = EXCLUDED.timeline_data";
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", record.AssessmentId);
        cmd.Parameters.AddWithValue("version", record.Version);
        cmd.Parameters.AddWithValue("project", record.ProjectName);
        cmd.Parameters.AddWithValue("template", record.TemplateName);
        cmd.Parameters.AddWithValue("generated", record.GeneratedAt);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Text, json);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        // 1. Ensure 'version' column exists
        const string checkColSql = @"
            DO $$ 
            BEGIN 
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                               WHERE table_name='assessment_timelines' AND column_name='version') THEN 
                    ALTER TABLE assessment_timelines ADD COLUMN version INT DEFAULT 0; 
                END IF; 
            END $$;";
        await using (var cmd = new NpgsqlCommand(checkColSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. Ensure PK includes version
        // We check if the PK constraint is named 'assessment_timelines_pkey'.
        // If it exists, we check columns. If it is just 'assessment_id', we drop and recreate.
        // This is a bit complex in SQL script, so we'll just try to drop strictly if it conflicts?
        // Simpler: Just try to add the column to PK. Hard to do idempotently without PL/pgSQL.
        
        const string checkPkSql = @"
            DO $$
            DECLARE
                pk_cols text;
            BEGIN
                -- Get columns in PK
                SELECT string_agg(a.attname, ',') INTO pk_cols
                FROM   pg_index i
                JOIN   pg_attribute a ON a.attrelid = i.indrelid
                                     AND a.attnum = ANY(i.indkey)
                WHERE  i.indrelid = 'assessment_timelines'::regclass
                AND    i.indisprimary;

                -- If PK exists and does NOT contain 'version', rebuild it
                IF pk_cols IS NOT NULL AND pk_cols NOT LIKE '%version%' THEN
                    ALTER TABLE assessment_timelines DROP CONSTRAINT assessment_timelines_pkey;
                    ALTER TABLE assessment_timelines ADD PRIMARY KEY (assessment_id, version);
                END IF;
            END $$;";
            
        await using (var cmd = new NpgsqlCommand(checkPkSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task LogGenerationAttemptAsync(TimelineGenerationAttempt attempt, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO assessment_timeline_attempts (
                                   assessment_id,
                                   project_name,
                                   template_name,
                                   requested_at,
                                   raw_response,
                                   error,
                                   success)
                             VALUES (@id, @project, @template, @requested, @raw, @error, @success)";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", attempt.AssessmentId);
        cmd.Parameters.AddWithValue("project", attempt.ProjectName ?? string.Empty);
        cmd.Parameters.AddWithValue("template", attempt.TemplateName ?? string.Empty);
        cmd.Parameters.AddWithValue("requested", attempt.RequestedAt);
        cmd.Parameters.Add("raw", NpgsqlDbType.Text).Value = attempt.RawResponse ?? string.Empty;
        cmd.Parameters.Add("error", NpgsqlDbType.Text).Value = (object?)attempt.Error ?? DBNull.Value;
        cmd.Parameters.AddWithValue("success", attempt.Success);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimelineRecord?> GetAsync(int assessmentId, int? version, CancellationToken cancellationToken)
    {
        // If version is null, get the latest (highest version)
        string sql = version.HasValue
            ? @"SELECT timeline_data FROM assessment_timelines WHERE assessment_id=@id AND version=@version"
            : @"SELECT timeline_data FROM assessment_timelines WHERE assessment_id=@id ORDER BY version DESC LIMIT 1";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        if (version.HasValue)
        {
            cmd.Parameters.AddWithValue("version", version.Value);
        }

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
            var record = JsonSerializer.Deserialize<TimelineRecord>(json, JsonOptions);
            // Ensure version is set if deserialization missed it (legacy data)
            if (record != null && version.HasValue)
            {
                 record.Version = version.Value;
            }
            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize stored timeline for assessment {AssessmentId}.", assessmentId);
            return null;
        }
    }
    
    public async Task<List<TimelineRecord>> GetAllVersionsAsync(int assessmentId, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT timeline_data FROM assessment_timelines WHERE assessment_id=@id ORDER BY version ASC";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        
        var results = new List<TimelineRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                var json = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try 
                    {
                        var rec = JsonSerializer.Deserialize<TimelineRecord>(json, JsonOptions);
                        if (rec != null) results.Add(rec);
                    }
                    catch { /* Ignore invalid rows */ }
                }
            }
        }
        return results;
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

    public async Task DeleteAsync(int assessmentId, CancellationToken cancellationToken)
    {
        const string sql = @"DELETE FROM assessment_timelines WHERE assessment_id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
