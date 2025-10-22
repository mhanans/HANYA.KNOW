using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssessmentController : ControllerBase
{
    private readonly ProjectTemplateStore _templates;
    private readonly ProjectAssessmentStore _assessments;
    private readonly ProjectAssessmentAnalysisService _analysisService;
    private readonly ILogger<AssessmentController> _logger;

    public AssessmentController(
        ProjectTemplateStore templates,
        ProjectAssessmentStore assessments,
        ProjectAssessmentAnalysisService analysisService,
        ILogger<AssessmentController> logger)
    {
        _templates = templates;
        _assessments = assessments;
        _analysisService = analysisService;
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

        try
        {
            var job = await _analysisService.AnalyzeAsync(
                template,
                request.TemplateId,
                request.ProjectName ?? string.Empty,
                request.File!,
                referenceAssessments,
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

    [HttpDelete("jobs/{jobId}")]
    [UiAuthorize("pre-sales-assessment-workspace")]
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
    [UiAuthorize("pre-sales-assessment-workspace")]
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

    [HttpGet("history")]
    [UiAuthorize("pre-sales-assessment-workspace")]
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
        var bytes = BuildExport(assessment, template);
        var fileName = $"assessment-{id}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static byte[] BuildExport(ProjectAssessment assessment, ProjectTemplate? template)
    {
        var columns = (template?.EstimationColumns?.Count ?? 0) > 0
            ? template!.EstimationColumns
            : assessment.Sections
                .SelectMany(s => s.Items)
                .SelectMany(i => i.Estimates.Keys)
                .Distinct()
                .ToList();

        IDictionary<string, object?> CreateRow()
        {
            var row = new Dictionary<string, object?>
            {
                ["Section"] = string.Empty,
                ["Item"] = string.Empty,
                ["Detail"] = string.Empty
            };

            foreach (var column in columns)
            {
                row[column] = string.Empty;
            }

            row["Total Manhours"] = string.Empty;
            return row;
        }

        var rows = new List<IDictionary<string, object?>>();
        var grandTotals = columns.ToDictionary(col => col, _ => 0.0);
        double grandTotalHours = 0;

        var metadataRow = CreateRow();
        metadataRow["Section"] = "Assessment Project Name";
        metadataRow["Item"] = assessment.ProjectName;
        rows.Add(metadataRow);

        var assessedOn = assessment.LastModifiedAt ?? assessment.CreatedAt;
        if (assessedOn.HasValue)
        {
            var dateRow = CreateRow();
            dateRow["Section"] = "Date Assessed";
            dateRow["Item"] = assessedOn.Value.ToString("yyyy-MM-dd");
            rows.Add(dateRow);
        }

        rows.Add(CreateRow());

        foreach (var section in assessment.Sections)
        {
            var sectionHeader = CreateRow();
            sectionHeader["Section"] = section.SectionName;
            rows.Add(sectionHeader);

            var sectionTotals = columns.ToDictionary(col => col, _ => 0.0);
            double sectionTotalHours = 0;

            foreach (var item in section.Items)
            {
                var row = CreateRow();
                row["Item"] = item.ItemName;
                row["Detail"] = item.ItemDetail;

                double itemTotal = 0;
                foreach (var column in columns)
                {
                    item.Estimates.TryGetValue(column, out var value);
                    var hours = value ?? 0;
                    row[column] = value;
                    sectionTotals[column] += hours;
                    grandTotals[column] += hours;
                    itemTotal += hours;
                }

                row["Total Manhours"] = itemTotal;
                sectionTotalHours += itemTotal;
                grandTotalHours += itemTotal;

                rows.Add(row);
            }

            var sectionTotalRow = CreateRow();
            sectionTotalRow["Section"] = $"{section.SectionName} · Total";
            foreach (var column in columns)
            {
                sectionTotalRow[column] = sectionTotals[column];
            }
            sectionTotalRow["Total Manhours"] = sectionTotalHours;
            rows.Add(sectionTotalRow);

            rows.Add(CreateRow());
        }

        var grandRow = CreateRow();
        grandRow["Section"] = "Grand Total";
        foreach (var column in columns)
        {
            grandRow[column] = grandTotals[column];
        }
        grandRow["Total Manhours"] = grandTotalHours;
        rows.Add(grandRow);

        using var stream = new MemoryStream();
        stream.SaveAs(rows);
        return stream.ToArray();
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
}

public class AssessmentAnalyzeRequest
{
    public int TemplateId { get; set; }
    public IFormFile? File { get; set; }
    public string? ProjectName { get; set; }
    public List<int> ReferenceAssessmentIds { get; set; } = new();
}
