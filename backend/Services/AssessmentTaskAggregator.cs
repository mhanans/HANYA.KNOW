using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using backend.Models;

namespace backend.Services;

public static class AssessmentTaskAggregator
{
    private static readonly Dictionary<string, string> DirectSectionPhaseMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Project Preparation"] = "Project Preparation",
        ["Project Closing (Project)"] = "Project Closing (Project)",
        ["Project Closing (Application)"] = "Project Closing (Application)",
        ["Post Go Live"] = "Post Go Live"
    };

    private static readonly Dictionary<string, string> LogicalPhaseMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Requirement & Documentation"] = "Analysis & Design",
        ["Requirement & Documentation (Creating SRS, FSD, and Disccussion)"] = "Analysis & Design",
        ["BE Development"] = "Development",
        ["FE Development"] = "Development",
        ["Architect Setup"] = "Architecture & Setup",
        ["Architect POC/ Research"] = "Architecture & Setup",
        ["SIT (Manual by QA)"] = "Testing & QA",
        ["SIT (with UFT)"] = "Testing & QA",
        ["UAT (With User)"] = "Testing & QA",
        ["Unit Test (Manual by QA)"] = "Testing & QA",
        ["UT (xUnit)"] = "Testing & QA",
        ["Code Review"] = "Testing & QA"
    };

    private static readonly object ColumnDiagnosticsLock = new();
    private static bool _hasLoggedColumnDiagnostics;

    public static Dictionary<string, double> AggregateEstimationColumnEffort(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (assessment?.Sections == null)
        {
            // Use Console.WriteLine for critical path debugging as it's not filtered by log levels.
            Console.WriteLine("[ERROR] AggregateEstimationColumnEffort: Assessment or Sections collection is null.");
            return result;
        }

        double totalHoursAggregated = 0;

        foreach (var section in assessment.Sections)
        {
            if (section?.Items == null) continue;

            foreach (var item in section.Items)
            {
                if (item == null || !item.IsNeeded || item.Estimates == null) continue;

                // The original code was correct, but we re-verify its logic.
                foreach (var estimate in item.Estimates)
                {
                    // Explicitly check the key and value before processing.
                    var columnName = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(columnName)) continue;

                    var hoursNullable = estimate.Value;
                    if (!hoursNullable.HasValue)
                    {
                        continue;
                    }

                    var hours = hoursNullable.Value;
                    if (hours <= 0)
                    {
                        continue;
                    }

                    var manDays = hours / 8.0;
                    totalHoursAggregated += hours;
                    result[columnName] = result.TryGetValue(columnName, out var existing) ? existing + manDays : manDays;
                }
            }
        }

        Console.WriteLine($"[DEBUG] AggregateEstimationColumnEffort finished. Total Hours: {totalHoursAggregated}. Total Man-Days: {result.Values.Sum()}. Number of Columns: {result.Count}.");
        return result;
    }

    private readonly record struct ItemEffortDetail(
        string SectionName,
        int SectionIndex,
        string ItemName,
        int ItemIndex,
        double ManDays);

    private static IEnumerable<ItemEffortDetail> EnumerateItemEffort(ProjectAssessment assessment)
    {
        if (assessment?.Sections == null)
        {
            yield break;
        }

        for (var sectionIndex = 0; sectionIndex < assessment.Sections.Count; sectionIndex++)
        {
            var section = assessment.Sections[sectionIndex];
            if (section == null)
            {
                continue;
            }

            var sectionName = section.SectionName?.Trim() ?? string.Empty;
            var items = section.Items ?? new List<AssessmentItem>();
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                if (item == null || !item.IsNeeded)
                {
                    continue;
                }

                var itemName = item.ItemName?.Trim() ?? string.Empty;
                double totalHours = 0;
                foreach (var estimate in item.Estimates ?? new Dictionary<string, double?>())
                {
                    if (estimate.Value is not double hours || hours <= 0)
                    {
                        continue;
                    }

                    totalHours += hours;
                }

                if (totalHours <= 0)
                {
                    continue;
                }

                var manDays = totalHours / 8d;
                if (manDays <= 0)
                {
                    continue;
                }

                yield return new ItemEffortDetail(sectionName, sectionIndex, itemName, itemIndex, manDays);
            }
        }
    }

    private static string BuildMappingKey(string sectionName, string itemName)
    {
        return $"{sectionName}\0{itemName}";
    }

    public static Dictionary<string, double> AggregateItemEffort(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var detail in EnumerateItemEffort(assessment))
        {
            if (string.IsNullOrWhiteSpace(detail.ItemName))
            {
                continue;
            }

            if (result.TryGetValue(detail.ItemName, out var existing))
            {
                result[detail.ItemName] = existing + detail.ManDays;
            }
            else
            {
                result[detail.ItemName] = detail.ManDays;
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateActivityManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        // This logic correctly consolidates all work into logical phases.
        _ = configuration;
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (assessment?.Sections == null) return result;

        foreach (var section in assessment.Sections)
        {
            var sectionName = section?.SectionName?.Trim() ?? string.Empty;
            foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded || item.Estimates == null) continue;
                foreach (var estimate in item.Estimates)
                {
                    if (estimate.Value is not double hours || hours <= 0) continue;
                    var manDays = hours / 8.0;
                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    var activityName = ResolveActivityName(sectionName, columnName);

                    result[activityName] = result.TryGetValue(activityName, out var current) ? current + manDays : manDays;
                }
            }
        }
        return result;
    }

    private static string ResolveActivityName(string sectionName, string columnName)
    {
        if (DirectSectionPhaseMappings.TryGetValue(sectionName, out var directPhase))
        {
            return directPhase;
        }
        if (!string.IsNullOrWhiteSpace(columnName) && LogicalPhaseMapping.TryGetValue(columnName, out var logicalPhase))
        {
            return logicalPhase;
        }
        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            return sectionName;
        }
        return "Uncategorized";
    }

    public static Dictionary<string, double> CalculateRoleManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var columnEffort = AggregateEstimationColumnEffort(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var columnRoleLookup = (configuration?.EstimationColumnRoles ?? Enumerable.Empty<EstimationColumnRoleMapping>())
            .Select(mapping => new
            {
                Column = mapping.EstimationColumn?.Trim(),
                Role = mapping.RoleName?.Trim()
            })
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.Column) &&
                !string.IsNullOrWhiteSpace(mapping.Role))
            .GroupBy(mapping => mapping.Column!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Role!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var uniqueColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmappedColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (columnName, manDays) in columnEffort)
        {
            var normalizedColumn = columnName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedColumn))
            {
                continue;
            }

            uniqueColumns.Add(normalizedColumn);

            if (!columnRoleLookup.TryGetValue(normalizedColumn, out var roles) || roles.Count == 0)
            {
                unmappedColumns.Add(normalizedColumn);

                const string unspecified = "Unassigned";
                result[unspecified] = result.TryGetValue(unspecified, out var existing)
                    ? existing + manDays
                    : manDays;
                continue;
            }

            var share = manDays / roles.Count;
            if (share <= 0)
            {
                continue;
            }

            foreach (var role in roles)
            {
                result[role] = result.TryGetValue(role, out var roleManDays)
                    ? roleManDays + share
                    : share;
            }
        }

        LogColumnDiagnostics(uniqueColumns, unmappedColumns);

        return result;
    }

    private static void LogColumnDiagnostics(
        IReadOnlyCollection<string> uniqueColumns,
        IReadOnlyCollection<string> unmappedColumns)
    {
        if (uniqueColumns.Count == 0)
        {
            return;
        }

        lock (ColumnDiagnosticsLock)
        {
            if (_hasLoggedColumnDiagnostics)
            {
                return;
            }

            var sortedColumns = uniqueColumns
                .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine(
                "[AssessmentTaskAggregator] Unique estimation columns detected: " +
                string.Join(", ", sortedColumns));

            if (unmappedColumns.Count > 0)
            {
                var sortedUnmapped = unmappedColumns
                    .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Console.WriteLine(
                    "[AssessmentTaskAggregator] Estimation columns without role mappings: " +
                    string.Join(", ", sortedUnmapped));
            }

            _hasLoggedColumnDiagnostics = true;
        }
    }
}
