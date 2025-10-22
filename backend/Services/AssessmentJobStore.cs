using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class AssessmentJobStore
{
    private readonly string _connectionString;
    private readonly ILogger<AssessmentJobStore> _logger;

    public AssessmentJobStore(IOptions<PostgresOptions> dbOptions, ILogger<AssessmentJobStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<int> InsertAsync(AssessmentJob job, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO assessment_jobs (
                                    project_name,
                                    template_id,
                                    status,
                                    scope_document_path,
                                    scope_document_mime_type,
                                    original_template_json,
                                    reference_assessments_json,
                                    raw_generation_response,
                                    generated_items_json,
                                    raw_estimation_response,
                                    final_analysis_json,
                                    last_error,
                                    created_at,
                                    last_modified_at)
                                VALUES (
                                    @projectName,
                                    @templateId,
                                    @status,
                                    @scopeDocumentPath,
                                    @scopeDocumentMimeType,
                                    @originalTemplateJson,
                                    @referenceAssessmentsJson,
                                    @rawGenerationResponse,
                                    @generatedItemsJson,
                                    @rawEstimationResponse,
                                    @finalAnalysisJson,
                                    @lastError,
                                    NOW(),
                                    NOW())
                                RETURNING id, created_at, last_modified_at";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectName", job.ProjectName ?? string.Empty);
        cmd.Parameters.AddWithValue("templateId", job.TemplateId);
        cmd.Parameters.AddWithValue("status", job.Status.ToString());
        cmd.Parameters.AddWithValue("scopeDocumentPath", job.ScopeDocumentPath ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentMimeType", job.ScopeDocumentMimeType ?? string.Empty);
        cmd.Parameters.Add("originalTemplateJson", NpgsqlDbType.Text).Value = job.OriginalTemplateJson ?? string.Empty;
        cmd.Parameters.Add("referenceAssessmentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceAssessmentsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawGenerationResponse", NpgsqlDbType.Text).Value = (object?)job.RawGenerationResponse ?? DBNull.Value;
        cmd.Parameters.Add("generatedItemsJson", NpgsqlDbType.Text).Value = (object?)job.GeneratedItemsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawEstimationResponse", NpgsqlDbType.Text).Value = (object?)job.RawEstimationResponse ?? DBNull.Value;
        cmd.Parameters.Add("finalAnalysisJson", NpgsqlDbType.Text).Value = (object?)job.FinalAnalysisJson ?? DBNull.Value;
        cmd.Parameters.Add("lastError", NpgsqlDbType.Text).Value = (object?)job.LastError ?? DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            job.Id = reader.GetInt32(0);
            job.CreatedAt = reader.GetDateTime(1);
            job.LastModifiedAt = reader.GetDateTime(2);
        }
        if (string.IsNullOrWhiteSpace(job.TemplateName))
        {
            job.TemplateName = await TryGetTemplateNameAsync(job.TemplateId, cancellationToken).ConfigureAwait(false);
        }

        return job.Id;
    }

    public async Task<AssessmentJob?> GetAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT aj.id,
                                     aj.project_name,
                                     aj.template_id,
                                     aj.status,
                                     aj.scope_document_path,
                                     aj.scope_document_mime_type,
                                     aj.original_template_json,
                                     aj.reference_assessments_json,
                                     aj.raw_generation_response,
                                     aj.generated_items_json,
                                     aj.raw_estimation_response,
                                     aj.final_analysis_json,
                                     aj.last_error,
                                     aj.created_at,
                                     aj.last_modified_at,
                                     COALESCE(pt.template_name, '') AS template_name
                              FROM assessment_jobs aj
                              LEFT JOIN project_templates pt ON pt.id = aj.template_id
                              WHERE aj.id=@id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpdateAsync(AssessmentJob job, CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE assessment_jobs
                              SET project_name=@projectName,
                                  template_id=@templateId,
                                  status=@status,
                                  scope_document_path=@scopeDocumentPath,
                                  scope_document_mime_type=@scopeDocumentMimeType,
                                  original_template_json=@originalTemplateJson,
                                  reference_assessments_json=@referenceAssessmentsJson,
                                  raw_generation_response=@rawGenerationResponse,
                                  generated_items_json=@generatedItemsJson,
                                  raw_estimation_response=@rawEstimationResponse,
                                  final_analysis_json=@finalAnalysisJson,
                                  last_error=@lastError,
                                  last_modified_at=NOW()
                              WHERE id=@id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", job.Id);
        cmd.Parameters.AddWithValue("projectName", job.ProjectName ?? string.Empty);
        cmd.Parameters.AddWithValue("templateId", job.TemplateId);
        cmd.Parameters.AddWithValue("status", job.Status.ToString());
        cmd.Parameters.AddWithValue("scopeDocumentPath", job.ScopeDocumentPath ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentMimeType", job.ScopeDocumentMimeType ?? string.Empty);
        cmd.Parameters.Add("originalTemplateJson", NpgsqlDbType.Text).Value = job.OriginalTemplateJson ?? string.Empty;
        cmd.Parameters.Add("referenceAssessmentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceAssessmentsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawGenerationResponse", NpgsqlDbType.Text).Value = (object?)job.RawGenerationResponse ?? DBNull.Value;
        cmd.Parameters.Add("generatedItemsJson", NpgsqlDbType.Text).Value = (object?)job.GeneratedItemsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawEstimationResponse", NpgsqlDbType.Text).Value = (object?)job.RawEstimationResponse ?? DBNull.Value;
        cmd.Parameters.Add("finalAnalysisJson", NpgsqlDbType.Text).Value = (object?)job.FinalAnalysisJson ?? DBNull.Value;
        cmd.Parameters.Add("lastError", NpgsqlDbType.Text).Value = (object?)job.LastError ?? DBNull.Value;

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
        {
            _logger.LogWarning("Attempted to update assessment job {JobId} but no rows were affected", job.Id);
        }
        job.LastModifiedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(job.TemplateName))
        {
            job.TemplateName = await TryGetTemplateNameAsync(job.TemplateId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<AssessmentJobSummary>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = @"SELECT aj.id,
                                     aj.project_name,
                                     aj.template_id,
                                     COALESCE(pt.template_name, '') AS template_name,
                                     aj.status,
                                     aj.created_at,
                                     aj.last_modified_at
                              FROM assessment_jobs aj
                              LEFT JOIN project_templates pt ON pt.id = aj.template_id
                              ORDER BY COALESCE(aj.last_modified_at, aj.created_at) DESC, aj.id DESC";

        var results = new List<AssessmentJobSummary>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new AssessmentJobSummary
            {
                Id = reader.GetInt32(0),
                ProjectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                TemplateId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                TemplateName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Status = ParseStatus(reader.IsDBNull(4) ? null : reader.GetString(4)),
                CreatedAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                LastModifiedAt = reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6)
            });
        }

        return results;
    }

    private static AssessmentJob Map(NpgsqlDataReader reader)
    {
        var status = ParseStatus(reader.IsDBNull(3) ? null : reader.GetString(3));

        return new AssessmentJob
        {
            Id = reader.GetInt32(0),
            ProjectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            TemplateId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            Status = status,
            ScopeDocumentPath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            ScopeDocumentMimeType = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            OriginalTemplateJson = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            ReferenceAssessmentsJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            RawGenerationResponse = reader.IsDBNull(8) ? null : reader.GetString(8),
            GeneratedItemsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
            RawEstimationResponse = reader.IsDBNull(10) ? null : reader.GetString(10),
            FinalAnalysisJson = reader.IsDBNull(11) ? null : reader.GetString(11),
            LastError = reader.IsDBNull(12) ? null : reader.GetString(12),
            CreatedAt = reader.IsDBNull(13) ? DateTime.UtcNow : reader.GetDateTime(13),
            LastModifiedAt = reader.IsDBNull(14) ? DateTime.UtcNow : reader.GetDateTime(14),
            TemplateName = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : string.Empty
        };
    }

    private static JobStatus ParseStatus(string? value)
    {
        if (Enum.TryParse<JobStatus>(value, out var status))
        {
            return status;
        }

        return JobStatus.Pending;
    }

    private async Task<string> TryGetTemplateNameAsync(int templateId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COALESCE(template_name, '') FROM project_templates WHERE id=@id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", templateId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string text ? text : string.Empty;
    }
}

