using System.Collections.Generic;
using System.IO;
using backend.Models;
using ClosedXML.Excel;

namespace backend.Services;

public static class TimelineExcelBuilder
{
    public static byte[] Build(TimelineRecord timeline)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Project Timeline");

        const int leftPaneColumns = 4;
        const int startColumn = leftPaneColumns + 1; // Column E
        var totalDays = Math.Max(timeline.TotalDurationDays, 0);

        worksheet.Column(1).Width = 25;
        worksheet.Column(2).Width = 35;
        worksheet.Column(3).Width = 20;
        worksheet.Column(4).Width = 12;

        for (var i = 0; i < totalDays; i++)
        {
            worksheet.Column(startColumn + i).Width = 3;
        }

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
            range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8F1FA"));
            range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        var monthRow = 1;
        var weekRow = 2;
        var dayRow = 3;
        var activityHeaderRow = 4;

        var columnPointer = startColumn;
        foreach (var month in months)
        {
            var range = worksheet.Range(monthRow, columnPointer, monthRow, columnPointer + month.Span - 1);
            range.Merge();
            range.Value = month.Label;
            ApplyMonthHeaderStyle(range);
            columnPointer += month.Span;
        }

        columnPointer = startColumn;
        foreach (var week in weeks)
        {
            var range = worksheet.Range(weekRow, columnPointer, weekRow, columnPointer + week.Span - 1);
            range.Merge();
            range.Value = week.Label;
            ApplyWeekHeaderStyle(range);
            columnPointer += week.Span;
        }

        for (var i = 0; i < totalDays; i++)
        {
            worksheet.Cell(dayRow, startColumn + i).Value = i + 1;
            worksheet.Cell(dayRow, startColumn + i).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            worksheet.Cell(dayRow, startColumn + i).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        worksheet.Cell(activityHeaderRow, 1).Value = "Activity";
        worksheet.Cell(activityHeaderRow, 2).Value = "Task";
        worksheet.Cell(activityHeaderRow, 3).Value = "Actor";
        worksheet.Cell(activityHeaderRow, 4).Value = "Duration (days)";
        worksheet.Range(activityHeaderRow, 1, activityHeaderRow, leftPaneColumns).Style.Font.SetBold();
        worksheet.Range(activityHeaderRow, 1, activityHeaderRow, leftPaneColumns).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#BDD7EE"));
        worksheet.Range(activityHeaderRow, 1, activityHeaderRow, leftPaneColumns).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        var currentRow = activityHeaderRow + 1;
        foreach (var activity in timeline.Activities ?? new List<TimelineActivity>())
        {
            var activityStartRow = currentRow;
            foreach (var detail in activity.Details ?? new List<TimelineDetail>())
            {
                worksheet.Cell(currentRow, 1).Value = activity.ActivityName;
                worksheet.Cell(currentRow, 2).Value = detail.TaskName;
                worksheet.Cell(currentRow, 3).Value = detail.Actor;
                worksheet.Cell(currentRow, 4).Value = detail.DurationDays;
                worksheet.Cell(currentRow, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                var detailRange = worksheet.Range(currentRow, 1, currentRow, leftPaneColumns);
                detailRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                detailRange.Style.Fill.SetBackgroundColor(activityFill);

                for (var day = 0; day < detail.DurationDays; day++)
                {
                    var column = startColumn + detail.StartDay - 1 + day;
                    var cell = worksheet.Cell(currentRow, column);
                    cell.Style.Fill.SetBackgroundColor(day % 2 == 0 ? greenFill : yellowFill);
                    cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                }

                currentRow++;
            }

            if (activityStartRow < currentRow - 1)
            {
                var mergeRange = worksheet.Range(activityStartRow, 1, currentRow - 1, 1);
                mergeRange.Merge();
                mergeRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                mergeRange.Style.Font.SetBold();
            }
        }

        var allocationStartRow = currentRow + 2;
        worksheet.Cell(allocationStartRow, 1).Value = "Resource Allocation";
        worksheet.Cell(allocationStartRow, 1).Style.Font.SetBold();

        var allocationHeaderRow = allocationStartRow + 1;
        worksheet.Cell(allocationHeaderRow, 1).Value = "Role";
        worksheet.Cell(allocationHeaderRow, 2).Value = "Total Man-days";
        worksheet.Cell(allocationHeaderRow, 3).Value = "Daily Effort";
        worksheet.Range(allocationHeaderRow, 1, allocationHeaderRow, 3).Style.Fill.SetBackgroundColor(blueFill);
        worksheet.Range(allocationHeaderRow, 1, allocationHeaderRow, 3).Style.Font.SetBold();
        worksheet.Range(allocationHeaderRow, 1, allocationHeaderRow, 3).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        var allocationRow = allocationHeaderRow + 1;
        foreach (var allocation in timeline.ResourceAllocation ?? new List<TimelineResourceAllocationEntry>())
        {
            worksheet.Cell(allocationRow, 1).Value = allocation.Role;
            worksheet.Cell(allocationRow, 2).Value = allocation.TotalManDays;
            worksheet.Cell(allocationRow, 3).Value = string.Join(", ", allocation.DailyEffort ?? new List<double>());

            var rowRange = worksheet.Range(allocationRow, 1, allocationRow, 3);
            rowRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            allocationRow++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
