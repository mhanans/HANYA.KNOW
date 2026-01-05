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
        job.SyncStepWithStatus();

        const string sql = @"INSERT INTO assessment_jobs (
                                    project_name,
                                    template_id,
                                    analysis_mode,
                                    output_language,
                                    status,
                                    step,
                                    scope_document_path,
                                    scope_document_mime_type,
                                    scope_document_has_manhour,
                                    detected_scope_manhour,
                                    manhour_detection_notes,
                                    original_template_json,
                                    reference_assessments_json,
                                    reference_documents_json,
                                    raw_generation_response,
                                    generated_items_json,
                                    raw_estimation_response,
                                    raw_manual_assessment_json,
                                    final_analysis_json,
                                    last_error,
                                    created_at,
                                    last_modified_at)
                                VALUES (
                                    @projectName,
                                    @templateId,
                                    @analysisMode,
                                    @outputLanguage,
                                    @status,
                                    @step,
                                    @scopeDocumentPath,
                                    @scopeDocumentMimeType,
                                    @scopeDocumentHasManhour,
                                    @detectedScopeManhour,
                                    @manhourDetectionNotes,
                                    @originalTemplateJson,
                                    @referenceAssessmentsJson,
                                    @referenceDocumentsJson,
                                    @rawGenerationResponse,
                                    @generatedItemsJson,
                                    @rawEstimationResponse,
                                    @rawManualAssessmentJson,
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
        cmd.Parameters.AddWithValue("analysisMode", job.AnalysisMode.ToString());
        cmd.Parameters.AddWithValue("outputLanguage", job.OutputLanguage.ToString());
        cmd.Parameters.AddWithValue("status", job.Status.ToString());
        cmd.Parameters.AddWithValue("step", job.Step);
        cmd.Parameters.AddWithValue("scopeDocumentPath", job.ScopeDocumentPath ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentMimeType", job.ScopeDocumentMimeType ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentHasManhour", job.ScopeDocumentHasManhour);
        cmd.Parameters.Add("detectedScopeManhour", NpgsqlDbType.Boolean).Value =
            (object?)job.DetectedScopeManhour ?? DBNull.Value;
        cmd.Parameters.Add("manhourDetectionNotes", NpgsqlDbType.Text).Value =
            (object?)job.ManhourDetectionNotes ?? DBNull.Value;
        cmd.Parameters.Add("originalTemplateJson", NpgsqlDbType.Text).Value = job.OriginalTemplateJson ?? string.Empty;
        cmd.Parameters.Add("referenceAssessmentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceAssessmentsJson ?? DBNull.Value;
        cmd.Parameters.Add("referenceDocumentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceDocumentsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawGenerationResponse", NpgsqlDbType.Text).Value = (object?)job.RawGenerationResponse ?? DBNull.Value;
        cmd.Parameters.Add("generatedItemsJson", NpgsqlDbType.Text).Value = (object?)job.GeneratedItemsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawEstimationResponse", NpgsqlDbType.Text).Value = (object?)job.RawEstimationResponse ?? DBNull.Value;
        cmd.Parameters.Add("rawManualAssessmentJson", NpgsqlDbType.Text).Value = (object?)job.RawManualAssessmentJson ?? DBNull.Value;
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
                                     aj.analysis_mode,
                                     aj.output_language,
                                     aj.status,
                                     aj.step,
                                     aj.scope_document_path,
                                     aj.scope_document_mime_type,
                                     aj.scope_document_has_manhour,
                                     aj.detected_scope_manhour,
                                     aj.manhour_detection_notes,
                                     aj.original_template_json,
                                     aj.reference_assessments_json,
                                     aj.reference_documents_json,
                                     aj.raw_generation_response,
                                     aj.generated_items_json,
                                     aj.raw_estimation_response,
                                     aj.raw_manual_assessment_json,
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
        job.SyncStepWithStatus();

        const string sql = @"UPDATE assessment_jobs
                              SET project_name=@projectName,
                                  template_id=@templateId,
                                  analysis_mode=@analysisMode,
                                  output_language=@outputLanguage,
                                  status=@status,
                                  step=@step,
                                  scope_document_path=@scopeDocumentPath,
                                  scope_document_mime_type=@scopeDocumentMimeType,
                                  scope_document_has_manhour=@scopeDocumentHasManhour,
                                  detected_scope_manhour=@detectedScopeManhour,
                                  manhour_detection_notes=@manhourDetectionNotes,
                                  original_template_json=@originalTemplateJson,
                                  reference_assessments_json=@referenceAssessmentsJson,
                                  reference_documents_json=@referenceDocumentsJson,
                                  raw_generation_response=@rawGenerationResponse,
                                  generated_items_json=@generatedItemsJson,
                                  raw_estimation_response=@rawEstimationResponse,
                                  raw_manual_assessment_json=@rawManualAssessmentJson,
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
        cmd.Parameters.AddWithValue("analysisMode", job.AnalysisMode.ToString());
        cmd.Parameters.AddWithValue("outputLanguage", job.OutputLanguage.ToString());
        cmd.Parameters.AddWithValue("status", job.Status.ToString());
        cmd.Parameters.AddWithValue("step", job.Step);
        cmd.Parameters.AddWithValue("scopeDocumentPath", job.ScopeDocumentPath ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentMimeType", job.ScopeDocumentMimeType ?? string.Empty);
        cmd.Parameters.AddWithValue("scopeDocumentHasManhour", job.ScopeDocumentHasManhour);
        cmd.Parameters.Add("detectedScopeManhour", NpgsqlDbType.Boolean).Value =
            (object?)job.DetectedScopeManhour ?? DBNull.Value;
        cmd.Parameters.Add("manhourDetectionNotes", NpgsqlDbType.Text).Value =
            (object?)job.ManhourDetectionNotes ?? DBNull.Value;
        cmd.Parameters.Add("originalTemplateJson", NpgsqlDbType.Text).Value = job.OriginalTemplateJson ?? string.Empty;
        cmd.Parameters.Add("referenceAssessmentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceAssessmentsJson ?? DBNull.Value;
        cmd.Parameters.Add("referenceDocumentsJson", NpgsqlDbType.Text).Value = (object?)job.ReferenceDocumentsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawGenerationResponse", NpgsqlDbType.Text).Value = (object?)job.RawGenerationResponse ?? DBNull.Value;
        cmd.Parameters.Add("generatedItemsJson", NpgsqlDbType.Text).Value = (object?)job.GeneratedItemsJson ?? DBNull.Value;
        cmd.Parameters.Add("rawEstimationResponse", NpgsqlDbType.Text).Value = (object?)job.RawEstimationResponse ?? DBNull.Value;
        cmd.Parameters.Add("rawManualAssessmentJson", NpgsqlDbType.Text).Value = (object?)job.RawManualAssessmentJson ?? DBNull.Value;
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
                                     aj.output_language,
                                     aj.status,
                                     aj.step,
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
                OutputLanguage = ParseLanguage(reader.IsDBNull(4) ? null : reader.GetString(4)),
                Status = ParseStatus(reader.IsDBNull(5) ? null : reader.GetString(5)),
                Step = reader.IsDBNull(6) ? 1 : Math.Max(1, reader.GetInt32(6)),
                CreatedAt = reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7),
                LastModifiedAt = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8)
            });
        }

        return results;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM assessment_jobs WHERE id=@id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    private static AssessmentJob Map(NpgsqlDataReader reader)
    {
        var analysisMode = ParseAnalysisMode(reader.IsDBNull(3) ? null : reader.GetString(3));
        var outputLanguage = ParseLanguage(reader.IsDBNull(4) ? null : reader.GetString(4));
        var status = ParseStatus(reader.IsDBNull(5) ? null : reader.GetString(5));

        return new AssessmentJob
        {
            Id = reader.GetInt32(0),
            ProjectName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            TemplateId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            AnalysisMode = analysisMode,
            OutputLanguage = outputLanguage,
            Status = status,
            Step = reader.IsDBNull(6) ? 1 : Math.Max(1, reader.GetInt32(6)),
            ScopeDocumentPath = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            ScopeDocumentMimeType = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            ScopeDocumentHasManhour = !reader.IsDBNull(9) && reader.GetBoolean(9),
            DetectedScopeManhour = reader.IsDBNull(10) ? null : reader.GetBoolean(10),
            ManhourDetectionNotes = reader.IsDBNull(11) ? null : reader.GetString(11),
            OriginalTemplateJson = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
            ReferenceAssessmentsJson = reader.IsDBNull(13) ? null : reader.GetString(13),
            ReferenceDocumentsJson = reader.IsDBNull(14) ? null : reader.GetString(14),
            RawGenerationResponse = reader.IsDBNull(15) ? null : reader.GetString(15),
            GeneratedItemsJson = reader.IsDBNull(16) ? null : reader.GetString(16),
            RawEstimationResponse = reader.IsDBNull(17) ? null : reader.GetString(17),
            RawManualAssessmentJson = reader.IsDBNull(18) ? null : reader.GetString(18),
            FinalAnalysisJson = reader.IsDBNull(19) ? null : reader.GetString(19),
            LastError = reader.IsDBNull(20) ? null : reader.GetString(20),
            CreatedAt = reader.IsDBNull(21) ? DateTime.UtcNow : reader.GetDateTime(21),
            LastModifiedAt = reader.IsDBNull(22) ? DateTime.UtcNow : reader.GetDateTime(22),
            TemplateName = reader.FieldCount > 23 && !reader.IsDBNull(23) ? reader.GetString(23) : string.Empty
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

    private static AssessmentAnalysisMode ParseAnalysisMode(string? value)
    {
        if (Enum.TryParse<AssessmentAnalysisMode>(value, true, out var mode))
        {
            return mode;
        }

        return AssessmentAnalysisMode.Interpretive;
    }

    private static AssessmentLanguage ParseLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AssessmentLanguage.Indonesian;
        }

        if (Enum.TryParse<AssessmentLanguage>(value, true, out var parsed))
        {
            return parsed;
        }

        if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "english", StringComparison.OrdinalIgnoreCase))
        {
            return AssessmentLanguage.English;
        }

        return AssessmentLanguage.Indonesian;
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

