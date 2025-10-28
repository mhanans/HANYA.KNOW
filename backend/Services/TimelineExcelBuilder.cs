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
        var allocationTitleRange = worksheet.Range(
            allocationStartRow,
            1,
            allocationStartRow,
            totalDays > 0 ? startColumn + totalDays - 1 : leftPaneColumns);
        allocationTitleRange.Merge();
        allocationTitleRange.Value = "Resource Allocation";
        allocationTitleRange.Style.Font.SetBold();

        var allocationHeaderRow = allocationStartRow + 1;
        var roleHeaderRange = worksheet.Range(allocationHeaderRow, 1, allocationHeaderRow, 3);
        roleHeaderRange.Merge();
        roleHeaderRange.Value = "Role";
        roleHeaderRange.Style.Fill.SetBackgroundColor(blueFill);
        roleHeaderRange.Style.Font.SetBold();
        roleHeaderRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        roleHeaderRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        roleHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        var totalHeaderCell = worksheet.Cell(allocationHeaderRow, 4);
        totalHeaderCell.Value = "Total Man-days";
        totalHeaderCell.Style.Fill.SetBackgroundColor(blueFill);
        totalHeaderCell.Style.Font.SetBold();
        totalHeaderCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        totalHeaderCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        totalHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        if (totalDays > 0)
        {
            var dailyHeaderRange = worksheet.Range(
                allocationHeaderRow,
                startColumn,
                allocationHeaderRow,
                startColumn + totalDays - 1);
            dailyHeaderRange.Merge();
            dailyHeaderRange.Value = "Daily Effort";
            dailyHeaderRange.Style.Fill.SetBackgroundColor(blueFill);
            dailyHeaderRange.Style.Font.SetBold();
            dailyHeaderRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            dailyHeaderRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            dailyHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        var allocationDayHeaderRow = allocationHeaderRow + 1;
        var roleDayHeaderRange = worksheet.Range(allocationDayHeaderRow, 1, allocationDayHeaderRow, 3);
        roleDayHeaderRange.Merge();
        roleDayHeaderRange.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F6FB"));
        roleDayHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        var totalDayHeaderCell = worksheet.Cell(allocationDayHeaderRow, 4);
        totalDayHeaderCell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F6FB"));
        totalDayHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        for (var i = 0; i < totalDays; i++)
        {
            var cell = worksheet.Cell(allocationDayHeaderRow, startColumn + i);
            cell.Value = i + 1;
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EEF3FB"));
            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        var allocationRow = allocationDayHeaderRow + 1;
        var resourceAllocations = timeline.ResourceAllocation ?? new List<TimelineResourceAllocationEntry>();
        for (var index = 0; index < resourceAllocations.Count; index++)
        {
            var allocation = resourceAllocations[index];
            var roleRange = worksheet.Range(allocationRow, 1, allocationRow, 3);
            roleRange.Merge();
            roleRange.Value = allocation.Role;
            roleRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            roleRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            roleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            roleRange.Style.Fill.SetBackgroundColor(index % 2 == 0 ? XLColor.FromHtml("#FFF9E6") : XLColor.FromHtml("#E9F2FB"));

            var totalCell = worksheet.Cell(allocationRow, 4);
            totalCell.Value = allocation.TotalManDays;
            totalCell.Style.NumberFormat.Format = "#,##0.00";
            totalCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            totalCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            totalCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            totalCell.Style.Fill.SetBackgroundColor(index % 2 == 0 ? XLColor.FromHtml("#FFF9E6") : XLColor.FromHtml("#E9F2FB"));

            for (var dayIndex = 0; dayIndex < totalDays; dayIndex++)
            {
                var effort = dayIndex < (allocation.DailyEffort?.Count ?? 0)
                    ? allocation.DailyEffort![dayIndex]
                    : 0d;
                var cell = worksheet.Cell(allocationRow, startColumn + dayIndex);
                if (effort > 0)
                {
                    cell.Value = effort;
                    cell.Style.NumberFormat.Format = "0.##";
                    cell.Style.Fill.SetBackgroundColor(index % 2 == 0 ? yellowFill : blueFill);
                }
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            }

            allocationRow++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
