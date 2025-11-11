using System;
using System.Collections.Generic;
using System.Linq;
using backend.Models;

namespace backend.Services;

public static class AssessmentTaskAggregator
{
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
        var itemEffortDetails = EnumerateItemEffort(assessment).ToList();
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

        foreach (var detail in itemEffortDetails)
        {
            var normalizedSection = detail.SectionName?.Trim() ?? string.Empty;
            var normalizedItem = detail.ItemName?.Trim() ?? string.Empty;

            var activity = string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedSection) &&
                !string.IsNullOrWhiteSpace(normalizedItem) &&
                sectionItemLookup.TryGetValue(BuildMappingKey(normalizedSection, normalizedItem), out var sectionItemActivity))
            {
                activity = sectionItemActivity;
            }
            else if (!string.IsNullOrWhiteSpace(normalizedItem) &&
                     itemLookup.TryGetValue(normalizedItem, out var mappedActivity))
            {
                activity = mappedActivity;
            }
            else if (!string.IsNullOrWhiteSpace(normalizedSection) &&
                     sectionLookup.TryGetValue(normalizedSection, out var sectionActivity))
            {
                activity = sectionActivity;
            }

            if (string.IsNullOrWhiteSpace(activity))
            {
                activity = "Unmapped";
            }

            if (result.TryGetValue(activity, out var current))
            {
                result[activity] = current + detail.ManDays;
            }
            else
            {
                result[activity] = detail.ManDays;
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

        foreach (var (columnName, manDays) in columnEffort)
        {
            var roles = configuration.EstimationColumnRoles
                .Where(tr => tr.EstimationColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                .Select(tr => tr.RoleName?.Trim())
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
