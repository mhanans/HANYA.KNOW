using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssessmentController : ControllerBase
{
    private readonly ProjectTemplateStore _templates;
    private readonly ProjectAssessmentStore _assessments;
    private readonly ProjectAssessmentAnalysisService _analysisService;
    private readonly AssessmentBundleExportService _bundleExport;
    private readonly VectorStore _vectorStore;
    private readonly ILogger<AssessmentController> _logger;

    public AssessmentController(
        ProjectTemplateStore templates,
        ProjectAssessmentStore assessments,
        ProjectAssessmentAnalysisService analysisService,
        AssessmentBundleExportService bundleExport,
        VectorStore vectorStore,
        ILogger<AssessmentController> logger)
    {
        _templates = templates;
        _assessments = assessments;
        _analysisService = analysisService;
        _bundleExport = bundleExport;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    [HttpPost("analyze")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<AssessmentJob>> Analyze([FromForm] AssessmentAnalyzeRequest request)
    {
        if (request.TemplateId <= 0)
        {
            return BadRequest("TemplateId is required");
        }

        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest("Scope document is required");
        }

        var template = await _templates.GetAsync(request.TemplateId);
        if (template == null)
        {
            return NotFound("Template not found");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        IReadOnlyList<ProjectAssessment>? referenceAssessments = null;

        if (request.ReferenceAssessmentIds.Count > 0)
        {
            referenceAssessments = await _assessments.GetByIdsAsync(request.ReferenceAssessmentIds, userIdValue);
        }

        if (referenceAssessments == null || referenceAssessments.Count == 0)
        {
            referenceAssessments = await _assessments.GetRecentAsync(userIdValue, limit: 3);
        }

        var referenceDocuments = await LoadReferenceDocumentsAsync(request.ReferenceDocumentSources, HttpContext.RequestAborted);
        var analysisMode = ParseAnalysisMode(request.AnalysisMode);
        var outputLanguage = ParseOutputLanguage(request.OutputLanguage);

        try
        {
            var job = await _analysisService.AnalyzeAsync(
                template,
                request.TemplateId,
                request.ProjectName ?? string.Empty,
                request.File!,
                analysisMode,
                outputLanguage,
                referenceAssessments,
                referenceDocuments,
                HttpContext.RequestAborted);

            return Ok(job);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for template {TemplateId}: {Message}", request.TemplateId, ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error running AI analysis for template {TemplateId}", request.TemplateId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to analyze the scope document due to an unexpected server error.");
        }
    }

    private async Task<IReadOnlyList<AssessmentReferenceDocument>> LoadReferenceDocumentsAsync(
        IEnumerable<string> sources,
        CancellationToken cancellationToken)
    {
        var distinct = sources
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => source.Trim())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return Array.Empty<AssessmentReferenceDocument>();
        }

        var documents = new List<AssessmentReferenceDocument>(distinct.Count);
        foreach (var source in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? summary = null;
            try
            {
                summary = await _vectorStore.GetDocumentSummaryAsync(source).ConfigureAwait(false);
            }
            catch
            {
                summary = null;
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                try
                {
                    summary = await _vectorStore.GetDocumentPreviewAsync(source).ConfigureAwait(false);
                }
                catch
                {
                    summary = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                documents.Add(new AssessmentReferenceDocument
                {
                    Source = source,
                    Summary = summary
                });
            }
        }

        return documents;
    }

    private static AssessmentAnalysisMode ParseAnalysisMode(string? value)
    {
        if (Enum.TryParse<AssessmentAnalysisMode>(value, true, out var mode))
        {
            return mode;
        }

        return AssessmentAnalysisMode.Interpretive;
    }

    private static AssessmentLanguage ParseOutputLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AssessmentLanguage.Indonesian;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<AssessmentLanguage>(trimmed, true, out var parsed))
        {
            return parsed;
        }

        if (string.Equals(trimmed, "id", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "bahasa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "indonesia", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "bahasa indonesia", StringComparison.OrdinalIgnoreCase))
        {
            return AssessmentLanguage.Indonesian;
        }

        if (string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "english", StringComparison.OrdinalIgnoreCase))
        {
            return AssessmentLanguage.English;
        }

        return AssessmentLanguage.Indonesian;
    }

    [HttpDelete("jobs/{jobId}")]
    [UiAuthorize("pre-sales-assessment-workspace", "admin-presales-history")]
    public async Task<IActionResult> DeleteJob(int jobId)
    {
        var deleted = await _analysisService.DeleteJobAsync(jobId, HttpContext.RequestAborted);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("jobs")]
    [UiAuthorize("pre-sales-assessment-workspace", "admin-presales-history")]
    public async Task<ActionResult<IEnumerable<AssessmentJobSummary>>> ListJobs()
    {
        var jobs = await _analysisService.ListJobsAsync(HttpContext.RequestAborted);
        return Ok(jobs);
    }

    [HttpGet("jobs/{jobId}")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<AssessmentJob>> GetJob(int jobId)
    {
        var job = await _analysisService.GetJobAsync(jobId, HttpContext.RequestAborted);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    [HttpPost("jobs/{jobId}/resume")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<AssessmentJob>> ResumeJob(int jobId)
    {
        var job = await _analysisService.RepairAndResumeFailedStepAsync(jobId, HttpContext.RequestAborted);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    [HttpGet("jobs/{jobId}/assessment")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<ProjectAssessment>> GetAssessmentForJob(int jobId)
    {
        var assessment = await _analysisService.TryBuildAssessmentAsync(jobId, HttpContext.RequestAborted);
        if (assessment == null)
        {
            return NotFound();
        }

        return Ok(assessment);
    }

    [HttpPost("save")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<ProjectAssessment>> Save(ProjectAssessment assessment)
    {
        if (assessment.TemplateId <= 0)
        {
            return BadRequest("TemplateId is required");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var id = await _assessments.SaveAsync(assessment, userIdValue);
        var saved = await _assessments.GetAsync(id, userIdValue);
        return Ok(saved ?? assessment);
    }

    [HttpPost("{id}/status")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<ProjectAssessmentSummary>> UpdateStatus(int id, UpdateAssessmentStatusRequest request)
    {
        if (id <= 0)
        {
            return BadRequest("A valid assessment id is required.");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest("Status is required.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var assessment = await _assessments.GetAsync(id, userIdValue);
        if (assessment == null)
        {
            return NotFound();
        }

        assessment.Status = request.Status.Trim();
        await _assessments.SaveAsync(assessment, userIdValue);
        var updated = await _assessments.GetAsync(id, userIdValue);

        if (updated == null)
        {
            return Ok(BuildSummary(assessment));
        }

        return Ok(BuildSummary(updated));
    }

    [HttpGet("history")]
    [UiAuthorize("pre-sales-assessment-workspace", "admin-presales-history")]
    public async Task<ActionResult<IEnumerable<ProjectAssessmentSummary>>> History()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var items = await _assessments.ListAsync(userIdValue);
        return Ok(items);
    }

    [HttpGet("template/{templateId}/similar")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<IEnumerable<SimilarAssessmentReference>>> SimilarByTemplate(int templateId)
    {
        if (templateId <= 0)
        {
            return BadRequest("TemplateId is required");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var assessments = await _assessments.GetRecentByTemplateAsync(templateId, userIdValue, limit: 5);
        return Ok(BuildReferenceSummaries(assessments));
    }

    [HttpGet("references")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<IEnumerable<SimilarAssessmentReference>>> References()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var assessments = await _assessments.GetRecentAsync(userIdValue, limit: 5);
        return Ok(BuildReferenceSummaries(assessments));
    }

    [HttpGet("{id}")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<ProjectAssessment>> Get(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var assessment = await _assessments.GetAsync(id, userIdValue);
        if (assessment == null)
        {
            return NotFound();
        }
        return Ok(assessment);
    }

    [HttpDelete("{id}")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var deleted = await _assessments.DeleteAsync(id, userIdValue);
        if (!deleted)
        {
            return NotFound();
        }
        return NoContent();
    }

    [HttpGet("{id}/export")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<IActionResult> Export(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;
        var assessment = await _assessments.GetAsync(id, userIdValue);
        if (assessment == null)
        {
            return NotFound();
        }

        var template = await _templates.GetAsync(assessment.TemplateId);
        var bytes = AssessmentExportBuilder.Build(assessment, template);
        var fileName = $"assessment-{id}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet("{id}/export/full")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<IActionResult> ExportAll(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdValue = int.TryParse(userId, out var uid) ? uid : (int?)null;

        try
        {
            var bytes = await _bundleExport.BuildCombinedWorkbookAsync(id, userIdValue, HttpContext.RequestAborted).ConfigureAwait(false);
            var fileName = $"assessment-{id}-bundle.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static List<SimilarAssessmentReference> BuildReferenceSummaries(IEnumerable<ProjectAssessment> assessments)
    {
        return assessments
            .Where(a => a.Id.HasValue)
            .Select(a => new SimilarAssessmentReference
            {
                Id = a.Id!.Value,
                TemplateId = a.TemplateId,
                TemplateName = a.TemplateName,
                ProjectName = a.ProjectName,
                Status = a.Status,
                TotalHours = CalculateTotalHours(a),
                LastModifiedAt = a.LastModifiedAt
            })
            .ToList();
    }

    private static double CalculateTotalHours(ProjectAssessment assessment)
    {
        double total = 0;
        foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
        {
            foreach (var item in section.Items ?? new List<AssessmentItem>())
            {
                if (item.Estimates == null)
                {
                    continue;
                }

                foreach (var value in item.Estimates.Values)
                {
                    if (value.HasValue)
                    {
                        total += value.Value;
                    }
                }
            }
        }

        return total;
    }

    private static ProjectAssessmentSummary BuildSummary(ProjectAssessment assessment)
    {
        return new ProjectAssessmentSummary
        {
            Id = assessment.Id ?? 0,
            TemplateId = assessment.TemplateId,
            TemplateName = assessment.TemplateName ?? string.Empty,
            ProjectName = assessment.ProjectName ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(assessment.Status) ? "Draft" : assessment.Status,
            Step = Math.Max(1, assessment.Step),
            CreatedAt = assessment.CreatedAt ?? DateTime.UtcNow,
            LastModifiedAt = assessment.LastModifiedAt
        };
    }
}

public class AssessmentAnalyzeRequest
{
    public int TemplateId { get; set; }
    public IFormFile? File { get; set; }
    public string? ProjectName { get; set; }
    public List<int> ReferenceAssessmentIds { get; set; } = new();
    public List<string> ReferenceDocumentSources { get; set; } = new();
    public string? AnalysisMode { get; set; }
    public string? OutputLanguage { get; set; }
}
