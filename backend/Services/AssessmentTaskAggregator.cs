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

    public static Dictionary<string, double> AggregateItemEffort(ProjectAssessment assessment)
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

                var itemName = item.ItemName?.Trim();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

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
                if (result.TryGetValue(itemName, out var existing))
                {
                    result[itemName] = existing + manDays;
                }
                else
                {
                    result[itemName] = manDays;
                }
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateActivityManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var itemEffort = AggregateItemEffort(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (itemName, manDays) in itemEffort)
        {
            var activity = configuration.ItemActivities
                .FirstOrDefault(ia => ia.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase))?.ActivityName
                ?? "Unmapped";
            if (string.IsNullOrWhiteSpace(activity))
            {
                activity = "Unmapped";
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
                .Select(tr => tr.RoleName)
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
