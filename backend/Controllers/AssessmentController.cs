using System.IO;
using System.Linq;
using System.Security.Claims;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MiniExcelLibs;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssessmentController : ControllerBase
{
    private readonly ProjectTemplateStore _templates;
    private readonly ProjectAssessmentStore _assessments;

    public AssessmentController(ProjectTemplateStore templates, ProjectAssessmentStore assessments)
    {
        _templates = templates;
        _assessments = assessments;
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

        var columns = template.EstimationColumns ?? new List<string>();
        var assessment = new ProjectAssessment
        {
            TemplateId = template.Id ?? request.TemplateId,
            Sections = template.Sections.Select(section => new AssessmentSection
            {
                SectionName = section.SectionName,
                Items = section.Items.Select(item => new AssessmentItem
                {
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    ItemDetail = item.ItemDetail,
                    IsNeeded = false,
                    Estimates = columns.ToDictionary(col => col, _ => (double?)null)
                }).ToList()
            }).ToList()
        };

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
        var id = await _assessments.SaveAsync(assessment, int.TryParse(userId, out var uid) ? uid : null);
        assessment.Id = id;
        return Ok(assessment);
    }

    [HttpGet("{id}/export")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<IActionResult> Export(int id)
    {
        var assessment = await _assessments.GetAsync(id);
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
}
