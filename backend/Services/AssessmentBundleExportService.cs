using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using ClosedXML.Excel;

namespace backend.Services;

public class AssessmentBundleExportService
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly ProjectTemplateStore _templates;
    private readonly TimelineStore _timelineStore;
    private readonly CostEstimationService _costEstimationService;
    private readonly CostEstimationStore _costEstimationStore;

    public AssessmentBundleExportService(
        ProjectAssessmentStore assessments,
        ProjectTemplateStore templates,
        TimelineStore timelineStore,
        CostEstimationService costEstimationService,
        CostEstimationStore costEstimationStore)
    {
        _assessments = assessments;
        _templates = templates;
        _timelineStore = timelineStore;
        _costEstimationService = costEstimationService;
        _costEstimationStore = costEstimationStore;
    }

    public async Task<byte[]> BuildCombinedWorkbookAsync(int assessmentId, int? userId, CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, userId).ConfigureAwait(false);
        if (assessment == null)
        {
            throw new KeyNotFoundException($"Assessment {assessmentId} was not found.");
        }

        using var workbook = new XLWorkbook();
        workbook.Worksheets.Clear();

        var template = await _templates.GetAsync(assessment.TemplateId).ConfigureAwait(false);
        var assessmentBytes = AssessmentExportBuilder.Build(assessment, template);
        AppendWorkbook(workbook, assessmentBytes);

        var timeline = await _timelineStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
        if (timeline != null)
        {
            var timelineBytes = TimelineExcelBuilder.Build(timeline);
            AppendWorkbook(workbook, timelineBytes);
        }

        var storedEstimation = await _costEstimationStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
        if (storedEstimation != null)
        {
            try
            {
                var estimationBytes = await _costEstimationService
                    .ExportAsync(assessmentId, storedEstimation.Inputs, cancellationToken)
                    .ConfigureAwait(false);
                AppendWorkbook(workbook, estimationBytes);
            }
            catch (KeyNotFoundException)
            {
                // Skip cost estimation sheet if data can no longer be generated
            }
            catch (InvalidOperationException)
            {
                // Skip if dependencies for export are no longer available
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AppendWorkbook(XLWorkbook target, byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var source = new XLWorkbook(stream);
        foreach (var sheet in source.Worksheets)
        {
            var name = GenerateUniqueName(target, sheet.Name);
            sheet.CopyTo(target, name);
        }
    }

    private static string GenerateUniqueName(XLWorkbook workbook, string baseName)
    {
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "Sheet" : baseName;
        var suffix = 1;
        while (workbook.Worksheets.Any(ws => ws.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            candidate = string.IsNullOrWhiteSpace(baseName) ? $"Sheet {suffix}" : $"{baseName} ({suffix})";
        }

        return candidate;
    }
}
