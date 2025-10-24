using System.Collections.Generic;
using System.IO;
using System.Linq;
using backend.Models;
using ClosedXML.Excel;

namespace backend.Services;

public static class AssessmentExportBuilder
{
    public static byte[] Build(ProjectAssessment assessment, ProjectTemplate? template)
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
}
