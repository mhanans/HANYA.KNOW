using System;
using System.Collections.Generic;
using System.Linq;
using backend.Models;

namespace backend.Services;

public static class AssessmentTaskAggregator
{
    public static Dictionary<string, (string DetailName, double ManDays)> AggregateTasks(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, (string, double)>(StringComparer.OrdinalIgnoreCase);
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

                    var taskKey = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(taskKey))
                    {
                        continue;
                    }

                    var manDays = hours / 8d;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    if (result.TryGetValue(taskKey, out var existing))
                    {
                        result[taskKey] = (existing.Item1, existing.Item2 + manDays);
                    }
                    else
                    {
                        var detailName = string.IsNullOrWhiteSpace(item.ItemDetail)
                            ? taskKey
                            : item.ItemDetail!;
                        result[taskKey] = (detailName, manDays);
                    }
                }
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateActivityManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var aggregatedTasks = AggregateTasks(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (taskKey, data) in aggregatedTasks)
        {
            var activity = configuration.TaskActivities
                .FirstOrDefault(ta => ta.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))?.ActivityName
                ?? "Unmapped";
            if (string.IsNullOrWhiteSpace(activity))
            {
                activity = "Unmapped";
            }

            if (result.TryGetValue(activity, out var current))
            {
                result[activity] = current + data.ManDays;
            }
            else
            {
                result[activity] = data.ManDays;
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateRoleManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var aggregatedTasks = AggregateTasks(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (taskKey, data) in aggregatedTasks)
        {
            var mappings = configuration.TaskRoles
                .Where(tr => tr.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (mappings.Count == 0)
            {
                const string unspecified = "Unassigned";
                result[unspecified] = result.TryGetValue(unspecified, out var existing)
                    ? existing + data.ManDays
                    : data.ManDays;
                continue;
            }

            foreach (var mapping in mappings)
            {
                var allocation = Math.Max(0, mapping.AllocationPercentage) / 100d;
                var allocatedManDays = allocation > 0 ? data.ManDays * allocation : 0d;
                if (allocatedManDays <= 0)
                {
                    continue;
                }

                result[mapping.RoleName] = result.TryGetValue(mapping.RoleName, out var roleManDays)
                    ? roleManDays + allocatedManDays
                    : allocatedManDays;
            }
        }

        return result;
    }
}
