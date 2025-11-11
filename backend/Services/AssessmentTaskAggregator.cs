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

    private static readonly (string Keyword, string Phase)[] EstimationColumnPhaseMappings =
    {
        ("requirement", "Analysis & Design"),
        ("documentation", "Analysis & Design"),
        ("analysis", "Analysis & Design"),
        ("design", "Analysis & Design"),
        ("architect setup", "Architecture & Setup"),
        ("architect poc", "Architecture & Setup"),
        ("architecture", "Architecture & Setup"),
        ("setup", "Architecture & Setup"),
        ("be development", "Development"),
        ("backend", "Development"),
        ("fe development", "Development"),
        ("frontend", "Development"),
        ("development", "Development"),
        ("code review", "Testing & QA"),
        ("unit test", "Testing & QA"),
        ("uat", "Testing & QA"),
        ("sit", "Testing & QA"),
        ("test", "Testing & QA"),
        ("deployment", "Deployment & Handover"),
        ("handover", "Deployment & Handover"),
        ("go live", "Deployment & Handover"),
        ("hypercare", "Post Go Live")
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

        var sectionItemLookup = configuration.ItemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(
                mapping => BuildMappingKey(mapping.SectionName.Trim(), mapping.ItemName.Trim()),
                mapping => mapping.ActivityName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var itemLookup = configuration.ItemActivities
            .Where(mapping =>
                string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(
                mapping => mapping.ItemName.Trim(),
                mapping => mapping.ActivityName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var sectionLookup = configuration.ItemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(
                mapping => mapping.SectionName.Trim(),
                mapping => mapping.ActivityName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections ?? Enumerable.Empty<AssessmentSection>())
        {
            var sectionName = section?.SectionName?.Trim() ?? string.Empty;
            foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
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

                    var manDays = hours / 8d;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    var phase = ResolvePhaseName(
                        sectionName,
                        itemName,
                        columnName,
                        sectionItemLookup,
                        itemLookup,
                        sectionLookup);

                    if (string.IsNullOrWhiteSpace(phase))
                    {
                        phase = string.IsNullOrWhiteSpace(sectionName) ? "Unmapped" : sectionName;
                    }

                    result[phase] = result.TryGetValue(phase, out var current)
                        ? current + manDays
                        : manDays;
                }
            }
        }

        return result;
    }

    private static string ResolvePhaseName(
        string sectionName,
        string itemName,
        string columnName,
        IReadOnlyDictionary<string, string> sectionItemLookup,
        IReadOnlyDictionary<string, string> itemLookup,
        IReadOnlyDictionary<string, string> sectionLookup)
    {
        if (!string.IsNullOrWhiteSpace(sectionName) &&
            DirectSectionPhaseMappings.TryGetValue(sectionName, out var directPhase))
        {
            return directPhase;
        }

        if (!string.IsNullOrWhiteSpace(columnName))
        {
            foreach (var (keyword, phase) in EstimationColumnPhaseMappings)
            {
                if (columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return phase;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(sectionName) &&
            !string.IsNullOrWhiteSpace(itemName) &&
            sectionItemLookup.TryGetValue(BuildMappingKey(sectionName, itemName), out var sectionItemActivity))
        {
            return sectionItemActivity;
        }

        if (!string.IsNullOrWhiteSpace(itemName) && itemLookup.TryGetValue(itemName, out var mappedActivity))
        {
            return mappedActivity;
        }

        if (!string.IsNullOrWhiteSpace(sectionName) && sectionLookup.TryGetValue(sectionName, out var sectionActivity))
        {
            return sectionActivity;
        }

        return string.Empty;
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
