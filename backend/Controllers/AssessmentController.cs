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
    public async Task<ActionResult<ProjectAssessment>> Analyze([FromForm] AssessmentAnalyzeRequest request)
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

        try
        {
            var assessment = await _analysisService.AnalyzeAsync(
                template,
                request.TemplateId,
                request.ProjectName ?? string.Empty,
                request.File!,
                HttpContext.RequestAborted);

            return Ok(assessment);
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

        var rows = new List<IDictionary<string, object?>>();
        var grandTotals = columns.ToDictionary(col => col, _ => 0.0);
        double grandTotalHours = 0;

        foreach (var section in assessment.Sections)
        {
            var sectionTotals = columns.ToDictionary(col => col, _ => 0.0);
            double sectionTotalHours = 0;

            foreach (var item in section.Items)
            {
                var row = new Dictionary<string, object?>
                {
                    ["Section"] = section.SectionName,
                    ["Item ID"] = item.ItemId,
                    ["Item Name"] = item.ItemName,
                    ["Item Detail"] = item.ItemDetail,
                    ["Needed?"] = item.IsNeeded ? "Yes" : "No"
                };

                double itemTotal = 0;
                foreach (var column in columns)
                {
                    item.Estimates.TryGetValue(column, out var value);
                    var hours = value ?? 0;
                    row[column] = value;
                    if (item.IsNeeded)
                    {
                        sectionTotals[column] += hours;
                        grandTotals[column] += hours;
                        itemTotal += hours;
                    }
                }

                row["Total Manhours"] = item.IsNeeded ? itemTotal : 0;
                if (item.IsNeeded)
                {
                    sectionTotalHours += itemTotal;
                    grandTotalHours += itemTotal;
                }

                rows.Add(row);
            }

            var sectionRow = new Dictionary<string, object?>
            {
                ["Section"] = $"{section.SectionName} · Total",
                ["Item ID"] = string.Empty,
                ["Item Name"] = string.Empty,
                ["Item Detail"] = string.Empty,
                ["Needed?"] = string.Empty
            };

            foreach (var column in columns)
            {
                sectionRow[column] = sectionTotals[column];
            }
            sectionRow["Total Manhours"] = sectionTotalHours;
            rows.Add(sectionRow);
        }

        var grandRow = new Dictionary<string, object?>
        {
            ["Section"] = "Grand Total",
            ["Item ID"] = string.Empty,
            ["Item Name"] = string.Empty,
            ["Item Detail"] = string.Empty,
            ["Needed?"] = string.Empty
        };
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
}

public class AssessmentAnalyzeRequest
{
    public int TemplateId { get; set; }
    public IFormFile? File { get; set; }
    public string? ProjectName { get; set; }
}
