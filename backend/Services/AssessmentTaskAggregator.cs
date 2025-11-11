using System;
using System.Collections.Generic;
using System.Linq;
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

    public static Dictionary<string, double> AggregateEstimationColumnEffort(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in assessment.Sections ?? Enumerable.Empty<AssessmentSection>())
        {
            foreach (var item in section.Items ?? Enumerable.Empty<AssessmentItem>())
            {
                if (!item.IsNeeded)
                {
                    continue;
                }

                foreach (var estimate in item.Estimates ?? new Dictionary<string, double?>())
                {
                    if (estimate.Value is not double hours || hours <= 0)
                    {
                        continue;
                    }

                    var columnName = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        continue;
                    }

                    var manDays = hours / 8d;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    if (result.TryGetValue(columnName, out var existing))
                    {
                        result[columnName] = existing + manDays;
                    }
                    else
                    {
                        result[columnName] = manDays;
                    }
                }
            }
        }

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
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections ?? Enumerable.Empty<AssessmentSection>())
        {
            var sectionName = section?.SectionName?.Trim() ?? "Uncategorized";

            if (DirectSectionPhaseMappings.TryGetValue(sectionName, out var directPhase))
            {
                foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
                {
                    if (item == null || !item.IsNeeded)
                    {
                        continue;
                    }

                    var totalHours = item.Estimates?.Values.Where(v => v.HasValue).Sum(v => v.Value) ?? 0;
                    if (totalHours <= 0)
                    {
                        continue;
                    }

                    var manDays = totalHours / 8d;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    result[directPhase] = result.TryGetValue(directPhase, out var current)
                        ? current + manDays
                        : manDays;
                }

                continue;
            }

            foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded)
                {
                    continue;
                }

                foreach (var estimate in item.Estimates ?? new Dictionary<string, double?>())
                {
                    if (estimate.Value is not double hours || hours <= 0)
                    {
                        continue;
                    }

                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        continue;
                    }

                    var logicalPhase = LogicalPhaseMapping.TryGetValue(columnName, out var phase)
                        ? phase
                        : "Development";

                    var manDays = hours / 8d;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    result[logicalPhase] = result.TryGetValue(logicalPhase, out var current)
                        ? current + manDays
                        : manDays;
                }
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateRoleManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var columnEffort = AggregateEstimationColumnEffort(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var columnRoleMappings = configuration?.EstimationColumnRoles ?? Enumerable.Empty<EstimationColumnRoleMapping>();

        foreach (var (columnName, manDays) in columnEffort)
        {
            var normalizedColumn = columnName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedColumn))
            {
                continue;
            }

            var roles = columnRoleMappings
                .Select(mapping => new
                {
                    Column = mapping.EstimationColumn?.Trim(),
                    Role = mapping.RoleName?.Trim()
                })
                .Where(mapping =>
                    !string.IsNullOrWhiteSpace(mapping.Column) &&
                    string.Equals(mapping.Column, normalizedColumn, StringComparison.OrdinalIgnoreCase))
                .Select(mapping => mapping.Role)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roles.Count == 0)
            {
                const string unspecified = "Unassigned";
                result[unspecified] = result.TryGetValue(unspecified, out var existing)
                    ? existing + manDays
                    : manDays;
                continue;
            }

            var share = manDays / roles.Count;
            foreach (var role in roles)
            {
                if (share <= 0)
                {
                    continue;
                }

                result[role] = result.TryGetValue(role, out var roleManDays)
                    ? roleManDays + share
                    : share;
            }
        }

        return result;
    }
}
