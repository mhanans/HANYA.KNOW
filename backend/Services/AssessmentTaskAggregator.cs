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
        if (assessment?.Sections == null)
        {
            return result;
        }

        var itemActivities = configuration?.ItemActivities ?? new List<ItemActivityMapping>();
        var columnActivityLookup = itemActivities
            .Where(mapping =>
                string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(mapping => mapping.ItemName.Trim(), mapping => mapping.ActivityName.Trim(), StringComparer.OrdinalIgnoreCase);

        var sectionItemActivityLookup = itemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(mapping => BuildMappingKey(mapping.SectionName, mapping.ItemName), mapping => mapping.ActivityName.Trim(), StringComparer.OrdinalIgnoreCase);

        var sectionActivityLookup = itemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(mapping => mapping.SectionName.Trim(), mapping => mapping.ActivityName.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections)
        {
            if (section == null)
            {
                continue;
            }

            var sectionName = section.SectionName?.Trim() ?? string.Empty;
            foreach (var item in section.Items ?? new List<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded)
                {
                    continue;
                }

                var itemName = item.ItemName?.Trim() ?? string.Empty;
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

                    var activity = ResolveActivityName(
                        columnName,
                        sectionName,
                        itemName,
                        columnActivityLookup,
                        sectionItemActivityLookup,
                        sectionActivityLookup);

                    var manDays = hours / 8d;
                    if (manDays <= 0 || string.IsNullOrWhiteSpace(activity))
                    {
                        continue;
                    }

                    if (result.TryGetValue(activity, out var current))
                    {
                        result[activity] = current + manDays;
                    }
                    else
                    {
                        result[activity] = manDays;
                    }
                }
            }
        }

        return result;
    }

    private static string ResolveActivityName(
        string columnName,
        string sectionName,
        string itemName,
        IReadOnlyDictionary<string, string> columnActivityLookup,
        IReadOnlyDictionary<string, string> sectionItemActivityLookup,
        IReadOnlyDictionary<string, string> sectionActivityLookup)
    {
        if (columnActivityLookup.TryGetValue(columnName, out var activityFromColumn) && !string.IsNullOrWhiteSpace(activityFromColumn))
        {
            return activityFromColumn;
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && DirectSectionPhaseMappings.TryGetValue(sectionName, out var directPhase))
        {
            return directPhase;
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && !string.IsNullOrWhiteSpace(itemName))
        {
            var key = BuildMappingKey(sectionName, itemName);
            if (sectionItemActivityLookup.TryGetValue(key, out var sectionItemActivity) && !string.IsNullOrWhiteSpace(sectionItemActivity))
            {
                return sectionItemActivity;
            }
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && sectionActivityLookup.TryGetValue(sectionName, out var activityFromSection) && !string.IsNullOrWhiteSpace(activityFromSection))
        {
            return activityFromSection;
        }

        if (LogicalPhaseMapping.TryGetValue(columnName, out var logicalPhase) && !string.IsNullOrWhiteSpace(logicalPhase))
        {
            return logicalPhase;
        }

        return string.IsNullOrWhiteSpace(columnName) ? "Uncategorized" : columnName;
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
