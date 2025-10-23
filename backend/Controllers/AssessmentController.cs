using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ClosedXML.Excel;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssessmentController : ControllerBase
{
    private readonly ProjectTemplateStore _templates;
    private readonly ProjectAssessmentStore _assessments;
    private readonly ProjectAssessmentAnalysisService _analysisService;
    private readonly VectorStore _vectorStore;
    private readonly ILogger<AssessmentController> _logger;

    public AssessmentController(
        ProjectTemplateStore templates,
        ProjectAssessmentStore assessments,
        ProjectAssessmentAnalysisService analysisService,
        VectorStore vectorStore,
        ILogger<AssessmentController> logger)
    {
        _templates = templates;
        _assessments = assessments;
        _analysisService = analysisService;
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

        try
        {
            var job = await _analysisService.AnalyzeAsync(
                template,
                request.TemplateId,
                request.ProjectName ?? string.Empty,
                request.File!,
                analysisMode,
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
        var bytes = BuildExport(assessment, template);
        var fileName = $"assessment-{id}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static byte[] BuildExport(ProjectAssessment assessment, ProjectTemplate? template)
    {
        var columns = (template?.EstimationColumns?.Count ?? 0) > 0
            ? template!.EstimationColumns.ToList()
            : (assessment.Sections ?? new List<AssessmentSection>())
                .SelectMany(s => s.Items ?? new List<AssessmentItem>())
                .SelectMany(i => (i.Estimates ?? new Dictionary<string, double?>()).Keys)
                .Distinct()
                .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Assessment");

        worksheet.Cell(1, 1).Value = "Assessment Project Name";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 2).Value = assessment.ProjectName ?? string.Empty;

        worksheet.Cell(2, 1).Value = "Date Assessed";
        worksheet.Cell(2, 1).Style.Font.Bold = true;
        var assessedOn = assessment.LastModifiedAt ?? assessment.CreatedAt;
        if (assessedOn.HasValue)
        {
            worksheet.Cell(2, 2).Value = assessedOn.Value.ToString("yyyy-MM-dd");
        }

        var headerRowNumber = 4;
        var headers = new List<string> { "Section", "Item", "Detail", "Category" };
        headers.AddRange(columns);
        headers.Add("Total Manhours");
        var totalColumnCount = headers.Count;
        var categoryColumnIndex = headers.IndexOf("Category") + 1;
        var firstEstimateColumnIndex = categoryColumnIndex + 1;

        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(headerRowNumber, index + 1).Value = headers[index];
        }

        var headerRange = worksheet.Range(headerRowNumber, 1, headerRowNumber, totalColumnCount);
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;

        worksheet.Column(1).Width = 25;
        worksheet.Column(2).Width = 30;
        worksheet.Column(3).Width = 50;
        worksheet.Column(3).Style.Alignment.WrapText = true;
        worksheet.Column(categoryColumnIndex).Width = 25;

        for (var columnIndex = firstEstimateColumnIndex; columnIndex <= totalColumnCount; columnIndex++)
        {
            worksheet.Column(columnIndex).Width = 15;
        }

        var currentRow = headerRowNumber + 1;
        var totalColumns = totalColumnCount;
        var totalColumnIndex = totalColumns;

        var grandTotals = columns.ToDictionary(col => col, _ => 0.0);
        double grandTotalHours = 0;

        foreach (var section in assessment.Sections ?? Enumerable.Empty<AssessmentSection>())
        {
            var sectionHeaderRange = worksheet.Range(currentRow, 1, currentRow, totalColumns);
            sectionHeaderRange.Merge();
            sectionHeaderRange.Value = section.SectionName ?? string.Empty;
            sectionHeaderRange.Style.Font.Bold = true;
            sectionHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
            sectionHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            sectionHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            currentRow++;

            var sectionTotals = columns.ToDictionary(col => col, _ => 0.0);
            double sectionTotalHours = 0;

            foreach (var item in section.Items ?? new List<AssessmentItem>())
            {
                worksheet.Cell(currentRow, 2).Value = item.ItemName ?? string.Empty;
                worksheet.Cell(currentRow, 3).Value = item.ItemDetail ?? string.Empty;
                worksheet.Cell(currentRow, categoryColumnIndex).Value = item.Category ?? string.Empty;

                double itemTotal = 0;
                var estimates = item.Estimates ?? new Dictionary<string, double?>();
                for (var index = 0; index < columns.Count; index++)
                {
                    var columnName = columns[index];
                    estimates.TryGetValue(columnName, out var value);
                    var hours = value ?? 0;
                    if (value.HasValue)
                    {
                        worksheet.Cell(currentRow, firstEstimateColumnIndex + index).Value = value.Value;
                    }

                    sectionTotals[columnName] += hours;
                    grandTotals[columnName] += hours;
                    itemTotal += hours;
                }

                worksheet.Cell(currentRow, totalColumnIndex).Value = itemTotal;
                sectionTotalHours += itemTotal;
                grandTotalHours += itemTotal;

                var itemRowRange = worksheet.Range(currentRow, 1, currentRow, totalColumns);
                itemRowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                worksheet.Cell(currentRow, categoryColumnIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                currentRow++;
            }

            worksheet.Cell(currentRow, 1).Value = $"{section.SectionName ?? string.Empty} · Total";
            worksheet.Cell(currentRow, 2).Value = "Totals";
            worksheet.Cell(currentRow, 3).Value = string.Empty;
            worksheet.Cell(currentRow, categoryColumnIndex).Value = string.Empty;

            for (var index = 0; index < columns.Count; index++)
            {
                worksheet.Cell(currentRow, firstEstimateColumnIndex + index).Value = sectionTotals[columns[index]];
            }

            worksheet.Cell(currentRow, totalColumnIndex).Value = sectionTotalHours;

            var sectionTotalRange = worksheet.Range(currentRow, 1, currentRow, totalColumns);
            sectionTotalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
            sectionTotalRange.Style.Font.Bold = true;
            sectionTotalRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            currentRow++;

            currentRow++;
        }

        worksheet.Cell(currentRow, 1).Value = "Grand Total";
        worksheet.Cell(currentRow, 2).Value = "Totals";
        worksheet.Cell(currentRow, 3).Value = string.Empty;
        worksheet.Cell(currentRow, categoryColumnIndex).Value = string.Empty;
        for (var index = 0; index < columns.Count; index++)
        {
            worksheet.Cell(currentRow, firstEstimateColumnIndex + index).Value = grandTotals[columns[index]];
        }

        worksheet.Cell(currentRow, totalColumnIndex).Value = grandTotalHours;

        var grandTotalRange = worksheet.Range(currentRow, 1, currentRow, totalColumns);
        grandTotalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
        grandTotalRange.Style.Font.Bold = true;
        grandTotalRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
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
    public List<string> ReferenceDocumentSources { get; set; } = new();
    public string? AnalysisMode { get; set; }
}
