using System;
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
        return job.Id;
    }

    public async Task<AssessmentJob?> GetAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT id,
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
                                     last_modified_at
                              FROM assessment_jobs
                              WHERE id=@id";

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
    }

    private static AssessmentJob Map(NpgsqlDataReader reader)
    {
        var statusText = reader.IsDBNull(3) ? JobStatus.Pending.ToString() : reader.GetString(3);
        Enum.TryParse<JobStatus>(statusText, out var status);

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
            LastModifiedAt = reader.IsDBNull(14) ? DateTime.UtcNow : reader.GetDateTime(14)
        };
    }
}

