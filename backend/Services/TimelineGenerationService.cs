using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class TimelineGenerationService
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly PresalesConfigurationStore _configurationStore;
    private readonly TimelineStore _timelineStore;
    private readonly ILogger<TimelineGenerationService> _logger;

    public TimelineGenerationService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        TimelineStore timelineStore,
        ILogger<TimelineGenerationService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _timelineStore = timelineStore;
        _logger = logger;
    }

    public async Task<TimelineRecord> GenerateAsync(int assessmentId, int? userId, CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, userId).ConfigureAwait(false);
        if (assessment == null)
        {
            throw new KeyNotFoundException($"Assessment {assessmentId} was not found.");
        }

        if (!string.Equals(assessment.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Timeline generation requires a completed assessment.");
        }

        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var activityLookup = config.Activities
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.ActivityName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(a => a.ActivityName, a => a, StringComparer.OrdinalIgnoreCase);

        var taskActivityLookup = config.TaskActivities
            .GroupBy(m => m.TaskKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.ActivityName).FirstOrDefault(), StringComparer.OrdinalIgnoreCase);

        var taskRoleLookup = config.TaskRoles
            .GroupBy(m => m.TaskKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var aggregatedTasks = AggregateTasks(assessment);
        if (aggregatedTasks.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline.");
        }

        var activities = new Dictionary<string, TimelineActivityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var (taskKey, detail) in aggregatedTasks.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            var activityName = taskActivityLookup.TryGetValue(taskKey, out var mappedActivity) && !string.IsNullOrWhiteSpace(mappedActivity)
                ? mappedActivity
                : "Unmapped Activities";

            if (!activities.TryGetValue(activityName, out var activityRecord))
            {
                var displayOrder = activityLookup.TryGetValue(activityName, out var activity)
                    ? activity.DisplayOrder
                    : int.MaxValue;
                activityRecord = new TimelineActivityRecord
                {
                    ActivityName = activityName,
                    DisplayOrder = displayOrder,
                    Details = new List<TimelineDetailRecord>()
                };
                activities[activityName] = activityRecord;
            }

            activityRecord.Details.Add(new TimelineDetailRecord
            {
                TaskKey = taskKey,
                DetailName = detail.DetailName,
                ManDays = detail.ManDays
            });
        }

        var orderedActivities = activities.Values
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.ActivityName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalDays = 0;
        foreach (var activity in orderedActivities)
        {
            foreach (var detail in activity.Details)
            {
                var duration = Math.Max(1, (int)Math.Ceiling(detail.ManDays));
                detail.DurationDays = duration;
                detail.StartDayIndex = totalDays;
                totalDays += duration;
            }
        }

        var workingDays = BuildWorkingDayTimeline(totalDays);
        foreach (var activity in orderedActivities)
        {
            foreach (var detail in activity.Details)
            {
                if (detail.StartDayIndex < workingDays.Count)
                {
                    detail.StartDate = workingDays[detail.StartDayIndex];
                    var endIndex = Math.Min(detail.StartDayIndex + detail.DurationDays - 1, workingDays.Count - 1);
                    detail.EndDate = workingDays[Math.Max(0, endIndex)];
                }
            }
        }

        var resourceAllocations = BuildResourceAllocations(orderedActivities, taskRoleLookup, config.Roles, totalDays, workingDays.Count);
        var manpowerSummary = BuildManpowerSummary(resourceAllocations, config.Roles);
        var totalCost = manpowerSummary.Sum(item => item.TotalCost);

        var record = new TimelineRecord
        {
            AssessmentId = assessmentId,
            ProjectName = assessment.ProjectName,
            TemplateName = assessment.TemplateName ?? string.Empty,
            GeneratedAt = DateTime.UtcNow,
            StartDate = workingDays.Count > 0 ? workingDays[0] : AlignToNextWorkingDay(DateTime.UtcNow.Date),
            WorkingDays = workingDays,
            Activities = orderedActivities,
            ManpowerSummary = manpowerSummary,
            ResourceAllocations = resourceAllocations,
            TotalCost = totalCost
        };

        _logger.LogInformation(
            "Generated timeline for assessment {AssessmentId} containing {ActivityCount} activities and {DayCount} working days.",
            assessmentId,
            orderedActivities.Count,
            workingDays.Count);

        await _timelineStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    private static Dictionary<string, (string DetailName, double ManDays)> AggregateTasks(ProjectAssessment assessment)
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

    private static List<DateTime> BuildWorkingDayTimeline(int totalDays)
    {
        var start = AlignToNextWorkingDay(DateTime.UtcNow.Date);
        var days = new List<DateTime>(Math.Max(1, totalDays));
        var cursor = start;
        while (days.Count < totalDays)
        {
            if (IsWorkingDay(cursor))
            {
                days.Add(cursor);
            }

            cursor = cursor.AddDays(1);
        }

        return days;
    }

    private static DateTime AlignToNextWorkingDay(DateTime date)
    {
        var aligned = date;
        while (!IsWorkingDay(aligned))
        {
            aligned = aligned.AddDays(1);
        }

        return aligned;
    }

    private static bool IsWorkingDay(DateTime date)
    {
        return date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }

    private static List<TimelineResourceAllocation> BuildResourceAllocations(
        IEnumerable<TimelineActivityRecord> activities,
        IReadOnlyDictionary<string, List<TaskRoleMapping>> taskRoleLookup,
        IEnumerable<PresalesRole> roles,
        int totalDays,
        int workingDayCount)
    {
        var roleLookup = roles.ToDictionary(r => r.RoleName, r => r, StringComparer.OrdinalIgnoreCase);
        var allocations = new Dictionary<string, TimelineResourceAllocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            allocations[role.RoleName] = new TimelineResourceAllocation
            {
                RoleName = role.RoleName,
                ExpectedLevel = role.ExpectedLevel,
                DailyAllocation = Enumerable.Repeat(0d, Math.Max(workingDayCount, totalDays)).ToList()
            };
        }

        foreach (var activity in activities)
        {
            foreach (var detail in activity.Details)
            {
                if (!taskRoleLookup.TryGetValue(detail.TaskKey, out var roleAssignments) || roleAssignments.Count == 0)
                {
                    continue;
                }

                var duration = Math.Max(1, detail.DurationDays);
                foreach (var assignment in roleAssignments)
                {
                    if (!allocations.TryGetValue(assignment.RoleName, out var allocation))
                    {
                        var roleInfo = roleLookup.TryGetValue(assignment.RoleName, out var r) ? r : new PresalesRole { RoleName = assignment.RoleName };
                        allocation = new TimelineResourceAllocation
                        {
                            RoleName = roleInfo.RoleName,
                            ExpectedLevel = roleInfo.ExpectedLevel,
                            DailyAllocation = Enumerable.Repeat(0d, Math.Max(workingDayCount, totalDays)).ToList()
                        };
                        allocations[assignment.RoleName] = allocation;
                    }

                    var allocatedManDays = detail.ManDays * (assignment.AllocationPercentage / 100d);
                    var perDay = allocatedManDays / duration;
                    for (var i = 0; i < duration; i++)
                    {
                        var dayIndex = detail.StartDayIndex + i;
                        if (dayIndex >= allocation.DailyAllocation.Count)
                        {
                            break;
                        }

                        allocation.DailyAllocation[dayIndex] += perDay;
                    }
                }
            }
        }

        return allocations.Values
            .Where(a => a.DailyAllocation.Any(value => value > 0))
            .OrderBy(a => a.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TimelineManpowerSummary> BuildManpowerSummary(
        IEnumerable<TimelineResourceAllocation> allocations,
        IEnumerable<PresalesRole> roles)
    {
        var roleLookup = roles.ToDictionary(r => r.RoleName, r => r, StringComparer.OrdinalIgnoreCase);
        var summaries = new List<TimelineManpowerSummary>();
        foreach (var allocation in allocations)
        {
            var roleName = allocation.RoleName;
            var totalManDays = allocation.DailyAllocation.Sum();
            roleLookup.TryGetValue(roleName, out var roleInfo);
            var costPerDay = roleInfo?.CostPerDay ?? 0;
            var totalCost = costPerDay * (decimal)totalManDays;
            summaries.Add(new TimelineManpowerSummary
            {
                RoleName = roleName,
                ExpectedLevel = roleInfo?.ExpectedLevel ?? string.Empty,
                ManDays = Math.Round(totalManDays, 2, MidpointRounding.AwayFromZero),
                CostPerDay = costPerDay,
                TotalCost = Math.Round(totalCost, 2, MidpointRounding.AwayFromZero)
            });
        }

        return summaries
            .OrderBy(s => s.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
