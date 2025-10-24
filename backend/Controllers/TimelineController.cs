using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using backend.Models;
using backend.Middleware;
using backend.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/timelines")]
public class TimelineController : ControllerBase
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly TimelineStore _timelineStore;
    private readonly TimelineGenerationService _generationService;

    public TimelineController(
        ProjectAssessmentStore assessments,
        TimelineStore timelineStore,
        TimelineGenerationService generationService)
    {
        _assessments = assessments;
        _timelineStore = timelineStore;
        _generationService = generationService;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<IEnumerable<TimelineAssessmentSummary>>> ListAssessments()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        var histories = await _assessments.ListAsync(userId).ConfigureAwait(false);
        var completed = histories
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var summaries = await _timelineStore.ListSummariesAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var results = completed
            .Select(a =>
            {
                summaries.TryGetValue(a.Id, out var existing);
                return new TimelineAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasTimeline = existing?.HasTimeline ?? false,
                    TimelineGeneratedAt = existing?.TimelineGeneratedAt
                };
            })
            .OrderByDescending(r => r.LastModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.AssessmentId)
            .ToList();

        return Ok(results);
    }

    [HttpPost]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineRecord>> GenerateTimeline(TimelineGenerationRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        try
        {
            var record = await _generationService.GenerateAsync(request.AssessmentId, userId, HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(record);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{assessmentId}")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineRecord>> GetTimeline(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var record = await _timelineStore.GetAsync(assessmentId, HttpContext.RequestAborted).ConfigureAwait(false);
        if (record == null)
        {
            return NotFound();
        }

        return Ok(record);
    }

    [HttpGet("{assessmentId}/export")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<IActionResult> ExportTimeline(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var record = await _timelineStore.GetAsync(assessmentId, HttpContext.RequestAborted).ConfigureAwait(false);
        if (record == null)
        {
            return NotFound("Timeline data not found for this assessment.");
        }

        var bytes = GenerateExcelFromTimeline(record);
        var safeProjectName = string.IsNullOrWhiteSpace(record.ProjectName)
            ? assessmentId.ToString()
            : record.ProjectName.Replace(" ", "_");
        var fileName = $"Timeline-{safeProjectName}.xlsx";

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static byte[] GenerateExcelFromTimeline(TimelineRecord timeline)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Project Timeline");

        const int leftPaneColumns = 4;
        const int startColumn = leftPaneColumns + 1; // Column E
        var totalDays = Math.Max(timeline.TotalDurationDays, 0);

        // Column widths for the fixed columns
        worksheet.Column(1).Width = 25;
        worksheet.Column(2).Width = 35;
        worksheet.Column(3).Width = 20;
        worksheet.Column(4).Width = 12;

        for (var i = 0; i < totalDays; i++)
        {
            worksheet.Column(startColumn + i).Width = 3;
        }

        // Compute the month and week groupings based on a 5-day week, 4-week month layout
        var weeks = new List<(string Label, int Span)>();
        var remainingDays = totalDays;
        var weekIndex = 1;
        while (remainingDays > 0)
        {
            var span = Math.Min(5, remainingDays);
            weeks.Add(($"W{weekIndex}", span));
            remainingDays -= span;
            weekIndex++;
        }

        var months = new List<(string Label, int Span)>();
        for (var i = 0; i < weeks.Count; i += 4)
        {
            var span = 0;
            for (var j = 0; j < 4 && i + j < weeks.Count; j++)
            {
                span += weeks[i + j].Span;
            }

            if (span > 0)
            {
                months.Add(($"Month {months.Count + 1}", span));
            }
        }

        var activityFill = XLColor.FromHtml("#F2F2F2");
        var greenFill = XLColor.FromHtml("#92D050");
        var yellowFill = XLColor.FromHtml("#FFF2CC");
        var blueFill = XLColor.FromHtml("#DEEBF7");

        static void ApplyMonthHeaderStyle(IXLRange range)
        {
            range.Style.Font.SetBold();
            range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#DDEBF7"));
            range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        static void ApplyWeekHeaderStyle(IXLRange range)
        {
            range.Style.Font.SetBold();
            range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#DDEBF7"));
            range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        static void ApplyHeaderStyle(IXLRange range)
        {
            range.Style.Font.SetBold();
            range.Style.Font.SetFontColor(XLColor.White);
            range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#4472C4"));
            range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        static void ApplySubHeaderStyle(IXLCell cell)
        {
            cell.Style.Font.SetBold();
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#DDEBF7"));
            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        static void ApplyDetailStyle(IXLRange range)
        {
            range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        var currentColumn = startColumn;
        foreach (var (label, span) in months)
        {
            var range = worksheet.Range(1, currentColumn, 1, currentColumn + span - 1);
            range.Merge();
            range.Value = label;
            ApplyMonthHeaderStyle(range);
            currentColumn += span;
        }

        currentColumn = startColumn;
        foreach (var (label, span) in weeks)
        {
            var range = worksheet.Range(2, currentColumn, 2, currentColumn + span - 1);
            range.Merge();
            range.Value = label;
            ApplyWeekHeaderStyle(range);
            currentColumn += span;
        }

        var headerRange = worksheet.Range(3, 1, 3, leftPaneColumns);
        headerRange.Value = new object[,]
        {
            { "Activity", "Detail", "Actor", "Man-days" }
        };
        ApplyHeaderStyle(headerRange);

        for (var day = 0; day < totalDays; day++)
        {
            var cell = worksheet.Cell(3, startColumn + day);
            cell.Value = day + 1;
            ApplySubHeaderStyle(cell);
        }

        var currentRow = 4;
        foreach (var activity in timeline.Activities)
        {
            for (var detailIndex = 0; detailIndex < activity.Details.Count; detailIndex++)
            {
                var detail = activity.Details[detailIndex];
                worksheet.Cell(currentRow, 1).Value = detailIndex == 0 ? activity.ActivityName : string.Empty;
                worksheet.Cell(currentRow, 2).Value = detail.TaskName;
                worksheet.Cell(currentRow, 3).Value = detail.Actor;
                worksheet.Cell(currentRow, 4).Value = detail.ManDays;
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0.00";

                var leftRange = worksheet.Range(currentRow, 1, currentRow, leftPaneColumns);
                ApplyDetailStyle(leftRange);
                worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = activityFill;
                if (detailIndex == 0)
                {
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                }

                if (detail.StartDay > 0)
                {
                    for (var offset = 0; offset < detail.DurationDays; offset++)
                    {
                        var column = startColumn + detail.StartDay - 1 + offset;
                        if (column < startColumn || column >= startColumn + totalDays)
                        {
                            continue;
                        }

                        var cell = worksheet.Cell(currentRow, column);
                        cell.Style.Fill.BackgroundColor = greenFill;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    }
                }

                currentRow++;
            }
        }

        currentRow += 2;

        var resourceHeaderRange = worksheet.Range(currentRow, 1, currentRow, 2);
        resourceHeaderRange.Cell(1, 1).Value = "Role";
        resourceHeaderRange.Cell(1, 2).Value = "Mandays Total";
        ApplyHeaderStyle(resourceHeaderRange);

        currentRow++;
        for (var index = 0; index < timeline.ResourceAllocation.Count; index++)
        {
            var allocation = timeline.ResourceAllocation[index];
            worksheet.Cell(currentRow, 1).Value = allocation.Role;
            worksheet.Cell(currentRow, 2).Value = allocation.TotalManDays;
            worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "#,##0.00";

            var resourceRange = worksheet.Range(currentRow, 1, currentRow, 2);
            ApplyDetailStyle(resourceRange);

            var fillColor = index % 2 == 0 ? yellowFill : blueFill;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = fillColor;

            for (var day = 0; day < allocation.DailyEffort.Count; day++)
            {
                if (day >= totalDays)
                {
                    break;
                }

                var effort = allocation.DailyEffort[day];
                if (effort <= 0)
                {
                    continue;
                }

                var cell = worksheet.Cell(currentRow, startColumn + day);
                cell.Value = effort;
                cell.Style.Fill.BackgroundColor = fillColor;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            currentRow++;
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange != null)
        {
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        worksheet.SheetView.FreezeRows(3);
        worksheet.SheetView.FreezeColumns(leftPaneColumns);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
