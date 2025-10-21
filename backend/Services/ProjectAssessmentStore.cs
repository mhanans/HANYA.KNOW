using System;
using System.Text.Json;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Services;

public class ProjectAssessmentStore
{
    private readonly string _connectionString;
    private readonly ILogger<ProjectAssessmentStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProjectAssessmentStore(IOptions<PostgresOptions> dbOptions, ILogger<ProjectAssessmentStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<ProjectAssessment?> GetAsync(int id)
    {
        const string sql = "SELECT template_id, assessment_data FROM project_assessments WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var templateId = reader.GetInt32(0);
        var json = reader.GetString(1);
        try
        {
            var assessment = JsonSerializer.Deserialize<ProjectAssessment>(json, JsonOptions);
            if (assessment != null)
            {
                assessment.Id = id;
                assessment.TemplateId = templateId;
            }
            return assessment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize assessment {AssessmentId}", id);
            throw;
        }
    }

    public async Task<int> SaveAsync(ProjectAssessment assessment, int? userId)
    {
        if (assessment.TemplateId <= 0)
        {
            throw new ArgumentException("TemplateId is required", nameof(assessment));
        }

        var payload = JsonSerializer.Serialize(assessment, JsonOptions);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        if (assessment.Id is null)
        {
            const string insertSql = @"INSERT INTO project_assessments (template_id, assessment_data, created_by_user_id, created_at, last_modified_at)
                                       VALUES (@templateId, @data, @user, NOW(), NOW()) RETURNING id";
            await using var cmd = new NpgsqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("templateId", assessment.TemplateId);
            cmd.Parameters.AddWithValue("data", payload);
            cmd.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        else
        {
            const string updateSql = @"UPDATE project_assessments
                                         SET assessment_data=@data, last_modified_at=NOW()
                                         WHERE id=@id";
            await using var cmd = new NpgsqlCommand(updateSql, conn);
            cmd.Parameters.AddWithValue("data", payload);
            cmd.Parameters.AddWithValue("id", assessment.Id.Value);
            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                throw new KeyNotFoundException($"Assessment {assessment.Id} not found");
            }
            return assessment.Id.Value;
        }
    }
}
