using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class ProjectAssessmentStore
{
    private readonly string _connectionString;
    private readonly ILogger<ProjectAssessmentStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _stepDefinitionsLock = new(1, 1);
    private IReadOnlyDictionary<string, int>? _statusStepMap;
    private int _maxStatusStep = 1;

    public ProjectAssessmentStore(IOptions<PostgresOptions> dbOptions, ILogger<ProjectAssessmentStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    private async Task EnsureStepDefinitionsAsync(CancellationToken cancellationToken)
    {
        if (_statusStepMap != null)
        {
            return;
        }

        await _stepDefinitionsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_statusStepMap != null)
            {
                return;
            }

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            const string sql = @"SELECT status, step FROM assessment_step_definitions ORDER BY step";

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var status = reader.IsDBNull(0) ? null : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(status))
                    {
                        continue;
                    }

                    var step = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    if (step < 1)
                    {
                        continue;
                    }

                    map[status.Trim()] = step;
                }
            }
            catch (PostgresException ex) when (string.Equals(ex.SqlState, PostgresErrorCodes.UndefinedTable, StringComparison.Ordinal))
            {
                _logger.LogWarning(ex, "Assessment step definitions table not found; defaulting to single-step workflow.");
                _statusStepMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _maxStatusStep = 1;
                return;
            }

            if (map.Count == 0)
            {
                _logger.LogWarning("No assessment step definitions found; defaulting to single-step workflow.");
                _statusStepMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _maxStatusStep = 1;
                return;
            }

            var orderedSteps = map.Values
                .Where(step => step > 0)
                .Distinct()
                .OrderBy(step => step)
                .ToList();

            if (orderedSteps.Count == 0)
            {
                _logger.LogWarning("Assessment step definitions query returned no positive step values; defaulting to single-step workflow.");
                _statusStepMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _maxStatusStep = 1;
                return;
            }

            var expectedSequence = Enumerable.Range(1, orderedSteps.Count).ToArray();
            var isSequential = orderedSteps.SequenceEqual(expectedSequence);
            if (!isSequential)
            {
                _logger.LogWarning("Assessment step definitions contain gaps. Expected sequential values {Expected} but found {Actual}.", string.Join(",", expectedSequence), string.Join(",", orderedSteps));
            }

            _statusStepMap = map;
            _maxStatusStep = orderedSteps.Last();
        }
        finally
        {
            _stepDefinitionsLock.Release();
        }
    }

    private async Task<int> NormalizeStepAsync(string status, int providedStep, CancellationToken cancellationToken)
    {
        await EnsureStepDefinitionsAsync(cancellationToken).ConfigureAwait(false);

        var map = _statusStepMap;
        var maxStep = Math.Max(1, _maxStatusStep);
        var baseStep = providedStep > 0 ? providedStep : 1;
        var cappedBase = Math.Min(Math.Max(1, baseStep), maxStep);

        if (!string.IsNullOrWhiteSpace(status) && map != null && map.TryGetValue(status.Trim(), out var mappedStep))
        {
            var normalized = Math.Min(Math.Max(1, mappedStep), maxStep);
            return Math.Max(cappedBase, normalized);
        }

        return cappedBase;
    }

    public async Task<ProjectAssessment?> GetAsync(int id, int? userId = null)
    {
        const string sql = @"SELECT pa.template_id,
                                     pa.project_name,
                                     pa.status,
                                     COALESCE(
                                         NULLIF(pa.assessment_data->>'step', '')::INT,
                                         NULLIF(pa.assessment_data->>'Step', '')::INT,
                                         1
                                     ) AS step,
                                     pa.assessment_data,
                                     pa.created_at,
                                     pa.last_modified_at,
                                     COALESCE(pt.template_name, '')
                              FROM project_assessments pa
                               LEFT JOIN project_templates pt ON pt.id = pa.template_id
                               WHERE pa.id=@id AND (@userId IS NULL OR pa.created_by_user_id=@userId)";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var templateId = reader.GetInt32(0);
        var projectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var status = reader.IsDBNull(2) ? "Draft" : reader.GetString(2);
        var step = reader.IsDBNull(3) ? 1 : reader.GetInt32(3);
        var json = reader.GetFieldValue<string>(4);
        var createdAt = reader.GetDateTime(5);
        var lastModifiedAt = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
        var templateName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
        try
        {
            var assessment = JsonSerializer.Deserialize<ProjectAssessment>(json, JsonOptions);
            if (assessment != null)
            {
                assessment.Id = id;
                assessment.TemplateId = templateId;
                assessment.TemplateName = templateName;
                assessment.CreatedAt = createdAt;
                assessment.LastModifiedAt = lastModifiedAt ?? createdAt;
                assessment.ProjectName = string.IsNullOrWhiteSpace(projectName)
                    ? string.Empty
                    : projectName;
                assessment.Status = string.IsNullOrWhiteSpace(status)
                    ? "Draft"
                    : status;
                assessment.Step = Math.Max(1, step);
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

        var projectName = assessment.ProjectName?.Trim() ?? string.Empty;
        var status = string.IsNullOrWhiteSpace(assessment.Status) ? "Draft" : assessment.Status.Trim();
        var step = await NormalizeStepAsync(status, assessment.Step, CancellationToken.None).ConfigureAwait(false);
        assessment.Step = step;
        var payload = JsonSerializer.Serialize(assessment, JsonOptions);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        if (assessment.Id is null)
        {
            const string insertSql = @"INSERT INTO project_assessments (template_id, project_name, status, assessment_data, created_by_user_id, created_at, last_modified_at)
                                       VALUES (@templateId, @projectName, @status, @data, @user, NOW(), NOW()) RETURNING id";
            await using var cmd = new NpgsqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("templateId", assessment.TemplateId);
            cmd.Parameters.AddWithValue("projectName", projectName);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.Add("data", NpgsqlDbType.Jsonb).Value = payload;
            cmd.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        else
        {
            const string updateSql = @"UPDATE project_assessments
                                         SET project_name=@projectName,
                                             status=@status,
                                             assessment_data=@data,
                                             last_modified_at=NOW()
                                         WHERE id=@id";
            await using var cmd = new NpgsqlCommand(updateSql, conn);
            cmd.Parameters.AddWithValue("projectName", projectName);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.Add("data", NpgsqlDbType.Jsonb).Value = payload;
            cmd.Parameters.AddWithValue("id", assessment.Id.Value);
            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                throw new KeyNotFoundException($"Assessment {assessment.Id} not found");
            }
            return assessment.Id.Value;
        }
    }

    public async Task<IReadOnlyList<ProjectAssessmentSummary>> ListAsync(int? userId)
    {
        const string sql = @"SELECT pa.id,
                                     pa.template_id,
                                     COALESCE(pt.template_name, '') AS template_name,
                                     COALESCE(pa.project_name, '') AS project_name,
                                     COALESCE(pa.status, 'Draft') AS status,
                                     COALESCE(
                                         NULLIF(pa.assessment_data->>'step', '')::INT,
                                         NULLIF(pa.assessment_data->>'Step', '')::INT,
                                         1
                                     ) AS step,
                                     pa.created_at,
                                     pa.last_modified_at
                              FROM project_assessments pa
                              LEFT JOIN project_templates pt ON pt.id = pa.template_id
                              WHERE (@userId IS NULL OR pa.created_by_user_id=@userId)
                              ORDER BY COALESCE(pa.last_modified_at, pa.created_at) DESC, pa.id DESC";

        var results = new List<ProjectAssessmentSummary>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var summary = new ProjectAssessmentSummary
            {
                Id = reader.GetInt32(0),
                TemplateId = reader.GetInt32(1),
                TemplateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ProjectName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Status = reader.IsDBNull(4) ? "Draft" : reader.GetString(4),
                Step = reader.IsDBNull(5) ? 1 : Math.Max(1, reader.GetInt32(5)),
                CreatedAt = reader.GetDateTime(6),
                LastModifiedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            };
            results.Add(summary);
        }
        return results;
    }

    public async Task<IReadOnlyList<ProjectAssessment>> GetRecentByTemplateAsync(int templateId, int? userId, int limit)
    {
        const string sql = @"SELECT pa.id,
                                     pa.template_id,
                                     COALESCE(pt.template_name, '') AS template_name,
                                     COALESCE(pa.project_name, '') AS project_name,
                                     COALESCE(pa.status, 'Draft') AS status,
                                     COALESCE(
                                         NULLIF(pa.assessment_data->>'step', '')::INT,
                                         NULLIF(pa.assessment_data->>'Step', '')::INT,
                                         1
                                     ) AS step,
                                     pa.assessment_data,
                                     pa.created_at,
                                     pa.last_modified_at
                              FROM project_assessments pa
                              LEFT JOIN project_templates pt ON pt.id = pa.template_id
                              WHERE pa.template_id=@templateId AND (@userId IS NULL OR pa.created_by_user_id=@userId)
                              ORDER BY COALESCE(pa.last_modified_at, pa.created_at) DESC, pa.id DESC
                              LIMIT @limit";

        var results = new List<ProjectAssessment>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("templateId", templateId);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var templId = reader.GetInt32(1);
            var templateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var projectName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var status = reader.IsDBNull(4) ? "Draft" : reader.GetString(4);
            var step = reader.IsDBNull(5) ? 1 : reader.GetInt32(5);
            var json = reader.GetFieldValue<string>(6);
            var createdAt = reader.GetDateTime(7);
            var lastModifiedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);

            try
            {
                var assessment = JsonSerializer.Deserialize<ProjectAssessment>(json, JsonOptions);
                if (assessment != null)
                {
                    assessment.Id = id;
                    assessment.TemplateId = templId;
                    assessment.TemplateName = templateName;
                    assessment.ProjectName = projectName;
                    assessment.Status = string.IsNullOrWhiteSpace(status) ? "Draft" : status;
                    assessment.Step = Math.Max(1, step);
                    assessment.CreatedAt = createdAt;
                    assessment.LastModifiedAt = lastModifiedAt ?? createdAt;
                    results.Add(assessment);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize assessment {AssessmentId}", id);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ProjectAssessment>> GetByIdsAsync(IEnumerable<int> ids, int? userId)
    {
        var idList = ids?.Distinct().Where(id => id > 0).ToList() ?? new List<int>();
        if (idList.Count == 0)
        {
            return Array.Empty<ProjectAssessment>();
        }

        const string sql = @"SELECT pa.id,
                                     pa.template_id,
                                     COALESCE(pt.template_name, '') AS template_name,
                                     COALESCE(pa.project_name, '') AS project_name,
                                     COALESCE(pa.status, 'Draft') AS status,
                                     COALESCE(
                                         NULLIF(pa.assessment_data->>'step', '')::INT,
                                         NULLIF(pa.assessment_data->>'Step', '')::INT,
                                         1
                                     ) AS step,
                                     pa.assessment_data,
                                     pa.created_at,
                                     pa.last_modified_at
                              FROM project_assessments pa
                              LEFT JOIN project_templates pt ON pt.id = pa.template_id
                              WHERE pa.id = ANY(@ids) AND (@userId IS NULL OR pa.created_by_user_id=@userId)";

        var results = new List<ProjectAssessment>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", idList);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var templateId = reader.GetInt32(1);
            var templateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var projectName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var status = reader.IsDBNull(4) ? "Draft" : reader.GetString(4);
            var step = reader.IsDBNull(5) ? 1 : reader.GetInt32(5);
            var json = reader.GetFieldValue<string>(6);
            var createdAt = reader.GetDateTime(7);
            var lastModifiedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);

            try
            {
                var assessment = JsonSerializer.Deserialize<ProjectAssessment>(json, JsonOptions);
                if (assessment != null)
                {
                    assessment.Id = id;
                    assessment.TemplateId = templateId;
                    assessment.TemplateName = templateName;
                    assessment.ProjectName = projectName;
                    assessment.Status = string.IsNullOrWhiteSpace(status) ? "Draft" : status;
                    assessment.Step = Math.Max(1, step);
                    assessment.CreatedAt = createdAt;
                    assessment.LastModifiedAt = lastModifiedAt ?? createdAt;
                    results.Add(assessment);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize assessment {AssessmentId}", id);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ProjectAssessment>> GetRecentAsync(int? userId, int limit)
    {
        const string sql = @"SELECT pa.id,
                                     pa.template_id,
                                     COALESCE(pt.template_name, '') AS template_name,
                                     COALESCE(pa.project_name, '') AS project_name,
                                     COALESCE(pa.status, 'Draft') AS status,
                                     COALESCE(
                                         NULLIF(pa.assessment_data->>'step', '')::INT,
                                         NULLIF(pa.assessment_data->>'Step', '')::INT,
                                         1
                                     ) AS step,
                                     pa.assessment_data,
                                     pa.created_at,
                                     pa.last_modified_at
                              FROM project_assessments pa
                              LEFT JOIN project_templates pt ON pt.id = pa.template_id
                              WHERE (@userId IS NULL OR pa.created_by_user_id=@userId)
                              ORDER BY COALESCE(pa.last_modified_at, pa.created_at) DESC, pa.id DESC
                              LIMIT @limit";

        var results = new List<ProjectAssessment>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var templateId = reader.GetInt32(1);
            var templateName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var projectName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var status = reader.IsDBNull(4) ? "Draft" : reader.GetString(4);
            var step = reader.IsDBNull(5) ? 1 : reader.GetInt32(5);
            var json = reader.GetFieldValue<string>(6);
            var createdAt = reader.GetDateTime(7);
            var lastModifiedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);

            try
            {
                var assessment = JsonSerializer.Deserialize<ProjectAssessment>(json, JsonOptions);
                if (assessment != null)
                {
                    assessment.Id = id;
                    assessment.TemplateId = templateId;
                    assessment.TemplateName = templateName;
                    assessment.ProjectName = projectName;
                    assessment.Status = string.IsNullOrWhiteSpace(status) ? "Draft" : status;
                    assessment.Step = Math.Max(1, step);
                    assessment.CreatedAt = createdAt;
                    assessment.LastModifiedAt = lastModifiedAt ?? createdAt;
                    results.Add(assessment);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize assessment {AssessmentId}", id);
            }
        }

        return results;
    }

    public async Task<bool> DeleteAsync(int id, int? userId)
    {
        const string sql = "DELETE FROM project_assessments WHERE id=@id AND (@userId IS NULL OR created_by_user_id=@userId)";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
