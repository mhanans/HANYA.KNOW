using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    private readonly TimelineEstimationStore _estimationStore;
    private readonly TimelineEstimationReferenceStore _timelineReferenceStore;
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineGenerationService> _logger;

    public TimelineGenerationService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        TimelineStore timelineStore,
        TimelineEstimationStore estimationStore,
        TimelineEstimationReferenceStore timelineReferenceStore,
        LlmClient llmClient,
        ILogger<TimelineGenerationService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _timelineStore = timelineStore;
        _estimationStore = estimationStore;
        _timelineReferenceStore = timelineReferenceStore;
        _llmClient = llmClient;
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
        var estimationColumnEffort = AssessmentTaskAggregator.AggregateEstimationColumnEffort(assessment);
        var aggregatedTasks = AssessmentTaskAggregator.AggregateItemEffort(assessment);
        if (estimationColumnEffort.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline.");
        }

        var estimatorRecord = await _estimationStore
            .GetAsync(assessmentId, cancellationToken)
            .ConfigureAwait(false);
        if (estimatorRecord == null)
        {
            throw new InvalidOperationException(
                "Timeline estimation must be generated before requesting a project timeline. " +
                "Run the Timeline Estimator step first to produce scale, phase duration, and headcount guidance.");
        }

        var referenceData = await _timelineReferenceStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var prompt = ConstructDailySchedulerAiPrompt(assessment, estimationColumnEffort, config, referenceData, estimatorRecord);
        _logger.LogInformation(
            "Requesting AI generated timeline for assessment {AssessmentId} with {TaskCount} tasks.",
            assessmentId,
            aggregatedTasks.Count);

        string rawResponse = string.Empty;
        AiTimelineResult aiTimeline;
        try
        {
            rawResponse = await _llmClient.GenerateAsync(prompt).ConfigureAwait(false);
            aiTimeline = ParseAiTimeline(rawResponse);
        }
        catch (JsonException ex)
        {
            await LogAttemptSafeAsync(
                assessmentId,
                assessment.ProjectName,
                assessment.TemplateName ?? string.Empty,
                rawResponse,
                success: false,
                error: $"JSON parse error: {ex.Message}",
                cancellationToken).ConfigureAwait(false);

            _logger.LogError(ex, "AI response was not valid JSON for timeline generation.");
            throw new InvalidOperationException("AI returned an invalid timeline response.", ex);
        }
        catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(rawResponse))
        {
            await LogAttemptSafeAsync(
                assessmentId,
                assessment.ProjectName,
                assessment.TemplateName ?? string.Empty,
                rawResponse,
                success: false,
                error: ex.Message,
                cancellationToken).ConfigureAwait(false);

            _logger.LogError(ex, "AI timeline response could not be parsed for assessment {AssessmentId}.", assessmentId);
            throw;
        }

        var record = new TimelineRecord
        {
            AssessmentId = assessmentId,
            ProjectName = assessment.ProjectName,
            TemplateName = assessment.TemplateName ?? string.Empty,
            GeneratedAt = DateTime.UtcNow,
            TotalDurationDays = aiTimeline.TotalDurationDays,
            Activities = aiTimeline.Activities,
            ResourceAllocation = aiTimeline.ResourceAllocation
        };

        record.ResourceAllocation = CalculateResourceAllocation(record, config);

        try
        {
            await _timelineStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await LogAttemptSafeAsync(
                assessmentId,
                assessment.ProjectName,
                assessment.TemplateName ?? string.Empty,
                rawResponse,
                success: false,
                error: $"Failed to persist timeline: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
            throw;
        }

        await LogAttemptSafeAsync(
            assessmentId,
            assessment.ProjectName,
            assessment.TemplateName ?? string.Empty,
            rawResponse,
            success: true,
            error: null,
            cancellationToken).ConfigureAwait(false);
        return record;
    }

    private Task LogAttemptSafeAsync(
        int assessmentId,
        string projectName,
        string templateName,
        string rawResponse,
        bool success,
        string? error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return Task.CompletedTask;
        }

        var attempt = new TimelineGenerationAttempt
        {
            AssessmentId = assessmentId,
            ProjectName = projectName,
            TemplateName = templateName,
            RequestedAt = DateTime.UtcNow,
            RawResponse = rawResponse,
            Success = success,
            Error = error
        };

        return LogAttemptInternalAsync(attempt, cancellationToken);
    }

    private async Task LogAttemptInternalAsync(TimelineGenerationAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            await _timelineStore.LogGenerationAttemptAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record timeline generation attempt for assessment {AssessmentId}.",
                attempt.AssessmentId);
        }
    }

    private static AiTimelineResult ParseAiTimeline(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("AI returned an empty timeline response.");
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        var jsonPayload = ExtractJsonPayload(response);
        var result = JsonSerializer.Deserialize<AiTimelineResult>(jsonPayload, options);
        if (result == null)
        {
            throw new InvalidOperationException("AI response could not be parsed into a timeline.");
        }

        result.Activities ??= new List<TimelineActivity>();
        result.ResourceAllocation ??= new List<TimelineResourceAllocationEntry>();
        return result;
    }

    private static string ExtractJsonPayload(string response)
    {
        var trimmed = response.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                var withoutFence = trimmed[(firstLineBreak + 1)..];
                var closingFenceIndex = withoutFence.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFenceIndex >= 0)
                {
                    var fencedContent = withoutFence[..closingFenceIndex].Trim();
                    if (!string.IsNullOrEmpty(fencedContent))
                    {
                        return fencedContent;
                    }
                }
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        return trimmed;
    }

    private static List<TimelineResourceAllocationEntry> CalculateResourceAllocation(
        TimelineRecord record,
        PresalesConfiguration config)
    {
        var activities = record.Activities ?? new List<TimelineActivity>();
        var allDetails = activities
            .SelectMany(a => a.Details ?? new List<TimelineDetail>())
            .ToList();

        var computedDuration = allDetails
            .Select(d => d.StartDay + d.DurationDays - 1)
            .Where(maxDay => maxDay > 0)
            .DefaultIfEmpty(record.TotalDurationDays)
            .Max();

        var totalDays = Math.Max(computedDuration, 0);
        if (totalDays > record.TotalDurationDays)
        {
            record.TotalDurationDays = totalDays;
        }

        var allocation = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        void EnsureRoleLength(string role)
        {
            if (!allocation.TryGetValue(role, out var list))
            {
                list = Enumerable.Repeat(0d, totalDays).ToList();
                allocation[role] = list;
            }
            else if (list.Count < totalDays)
            {
                while (list.Count < totalDays)
                {
                    list.Add(0d);
                }
            }
        }

        foreach (var roleLabel in config.Roles
                     .Select(role => PresalesRoleFormatter.BuildLabel(role.RoleName, role.ExpectedLevel))
                     .Where(label => !string.IsNullOrWhiteSpace(label)))
        {
            EnsureRoleLength(roleLabel);
        }

        foreach (var detail in allDetails)
        {
            if (detail.DurationDays <= 0)
            {
                continue;
            }

            var actors = (detail.Actor ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .DefaultIfEmpty(detail.Actor ?? string.Empty)
                .Where(actor => !string.IsNullOrWhiteSpace(actor))
                .ToList();

            if (actors.Count == 0)
            {
                continue;
            }

            var dailyEffort = detail.ManDays / detail.DurationDays;
            if (dailyEffort <= 0)
            {
                continue;
            }

            foreach (var actor in actors)
            {
                EnsureRoleLength(actor);
            }

            for (var dayOffset = 0; dayOffset < detail.DurationDays; dayOffset++)
            {
                var absoluteDay = detail.StartDay + dayOffset;
                if (absoluteDay <= 0 || absoluteDay > totalDays)
                {
                    continue;
                }

                var perRoleEffort = dailyEffort / actors.Count;
                foreach (var actor in actors)
                {
                    allocation[actor][absoluteDay - 1] += perRoleEffort;
                }
            }
        }

        static bool RoleMatches(string candidate, string target)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var baseRole = PresalesRoleFormatter.ExtractBaseRole(candidate);
            if (string.IsNullOrWhiteSpace(baseRole))
            {
                return false;
            }

            if (baseRole.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (target.Equals("PM", StringComparison.OrdinalIgnoreCase) &&
                baseRole.Equals("Project Manager", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        foreach (var specialRole in new[] { "PM", "Architect" })
        {
            var hasRole = allocation.Keys.Any(role => RoleMatches(role, specialRole)) ||
                          config.Roles.Any(r => RoleMatches(PresalesRoleFormatter.BuildLabel(r.RoleName, r.ExpectedLevel), specialRole));
            if (!hasRole)
            {
                continue;
            }

            EnsureRoleLength(specialRole);
            var values = allocation[specialRole];
            for (var day = 0; day < values.Count; day++)
            {
                values[day] = Math.Max(values[day], 0.5);
            }
        }

        var orderLookup = config.Roles
            .Select((role, index) => new { Label = PresalesRoleFormatter.BuildLabel(role.RoleName, role.ExpectedLevel), index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Label))
            .ToDictionary(x => x.Label, x => x.index, StringComparer.OrdinalIgnoreCase);

        var result = allocation
            .Select(kvp =>
            {
                var roundedDaily = kvp.Value.Select(v => Math.Round(v, 2)).ToList();
                var totalManDays = Math.Round(roundedDaily.Sum(), 2);
                return new TimelineResourceAllocationEntry
                {
                    Role = kvp.Key,
                    TotalManDays = totalManDays,
                    DailyEffort = roundedDaily
                };
            })
            .Where(entry => entry.TotalManDays > 0.01 || RoleMatches(entry.Role, "PM") || RoleMatches(entry.Role, "Architect"))
            .ToList();

        result.Sort((a, b) =>
        {
            var aHasOrder = orderLookup.TryGetValue(a.Role, out var aOrder);
            var bHasOrder = orderLookup.TryGetValue(b.Role, out var bOrder);
            if (aHasOrder && bHasOrder)
            {
                return aOrder.CompareTo(bOrder);
            }

            if (aHasOrder)
            {
                return -1;
            }

            if (bHasOrder)
            {
                return 1;
            }

            return string.Compare(a.Role, b.Role, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private string ConstructDailySchedulerAiPrompt(
        ProjectAssessment assessment,
        Dictionary<string, double> estimationColumnManDays,
        PresalesConfiguration config,
        IReadOnlyList<TimelineEstimationReference> references,
        TimelineEstimationRecord estimation)
    {
        static string BuildMappingKey(string? sectionName, string? itemName)
        {
            return $"{sectionName?.Trim() ?? string.Empty}\0{itemName?.Trim() ?? string.Empty}";
        }

        var sectionItemActivityLookup = config.ItemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(
                mapping => BuildMappingKey(mapping.SectionName, mapping.ItemName),
                mapping => mapping.ActivityName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var itemActivityLookup = config.ItemActivities
            .Where(mapping =>
                string.IsNullOrWhiteSpace(mapping.SectionName) &&
                !string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(mapping => mapping.ItemName.Trim(), mapping => mapping.ActivityName.Trim(), StringComparer.OrdinalIgnoreCase);

        var sectionActivityLookup = config.ItemActivities
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SectionName) &&
                string.IsNullOrWhiteSpace(mapping.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping.ActivityName))
            .ToDictionary(mapping => mapping.SectionName.Trim(), mapping => mapping.ActivityName.Trim(), StringComparer.OrdinalIgnoreCase);

        var activityOrderLookup = config.Activities
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityName))
            .Select(activity => new
            {
                Name = activity.ActivityName.Trim(),
                Order = activity.DisplayOrder
            })
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.Order), StringComparer.OrdinalIgnoreCase);

        var columnRolesLookup = config.EstimationColumnRoles
            .GroupBy(mapping => mapping.EstimationColumn, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key.Trim(),
                group => group
                    .Select(entry => entry.RoleName?.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var columnActivities = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
        {
            var sectionName = section.SectionName?.Trim();
            foreach (var item in section.Items ?? new List<AssessmentItem>())
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

                var activity = string.Empty;
                if (!string.IsNullOrWhiteSpace(sectionName) &&
                    !string.IsNullOrWhiteSpace(itemName) &&
                    sectionItemActivityLookup.TryGetValue(BuildMappingKey(sectionName, itemName), out var combinedActivity))
                {
                    activity = combinedActivity;
                }
                else if (!string.IsNullOrWhiteSpace(itemName) && itemActivityLookup.TryGetValue(itemName, out var mappedActivity))
                {
                    activity = mappedActivity;
                }
                else if (!string.IsNullOrWhiteSpace(sectionName) && sectionActivityLookup.TryGetValue(sectionName, out var sectionActivity))
                {
                    activity = sectionActivity;
                }
                else
                {
                    activity = "Unmapped";
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

                    if (!columnActivities.TryGetValue(columnName, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        columnActivities[columnName] = set;
                    }

                    if (!string.IsNullOrWhiteSpace(activity))
                    {
                        set.Add(activity);
                    }
                }
            }
        }

        var columnMetadata = estimationColumnManDays
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
            {
                var columnName = kvp.Key;
                var manDays = kvp.Value;
                var roles = columnRolesLookup.TryGetValue(columnName, out var mappedRoles)
                    ? mappedRoles
                    : new List<string>();
                var activities = columnActivities.TryGetValue(columnName, out var set) && set.Count > 0
                    ? set
                        .OrderBy(value => value, Comparer<string>.Create((a, b) =>
                        {
                            var aOrder = activityOrderLookup.TryGetValue(a, out var aValue) ? aValue : int.MaxValue;
                            var bOrder = activityOrderLookup.TryGetValue(b, out var bValue) ? bValue : int.MaxValue;
                            var orderComparison = aOrder.CompareTo(bOrder);
                            if (orderComparison != 0)
                            {
                                return orderComparison;
                            }

                            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                        }))
                        .ToList()
                    : new List<string> { "Unmapped" };

                var actorString = roles.Count > 0 ? string.Join(", ", roles) : "Unassigned";
                var activitySummary = activities.Count > 0 ? string.Join(", ", activities) : "Unmapped";

                return new
                {
                    Column = columnName,
                    ManDays = manDays,
                    Roles = roles,
                    ActorString = actorString,
                    Activities = new HashSet<string>(activities, StringComparer.OrdinalIgnoreCase),
                    ActivitySummary = activitySummary
                };
            })
            .ToList();

        var taskDetailsForPrompt = columnMetadata.Select(meta => string.Format(
            CultureInfo.InvariantCulture,
            "  - Estimation Column: \"{0}\" => {1:F2} man-days | Roles: \"{2}\" | Activities: \"{3}\"",
            meta.Column,
            meta.ManDays,
            meta.ActorString,
            meta.ActivitySummary));

        var activityManDays = AssessmentTaskAggregator.CalculateActivityManDays(assessment, config);
        var phaseSummaries = activityManDays
            .OrderBy(kvp => activityOrderLookup.TryGetValue(kvp.Key, out var order) ? order : int.MaxValue)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
            {
                var activityName = kvp.Key;
                var totalManHours = kvp.Value * 8d;
                var roles = columnMetadata
                    .Where(meta => meta.Activities.Contains(activityName))
                    .SelectMany(meta => meta.Roles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var roleSummary = roles.Count > 0 ? string.Join(", ", roles) : "Unassigned";
                var resourceCount = Math.Max(roles.Count, 1);
                return new
                {
                    ActivityGroup = activityName,
                    TotalManHours = totalManHours,
                    ResourceCount = resourceCount,
                    RoleSummary = roleSummary
                };
            })
            .ToList();

        var phaseSummaryText = phaseSummaries.Count > 0
            ? string.Join("\n", phaseSummaries.Select(summary =>
                $"    - Activity: \"{summary.ActivityGroup}\", TotalManHours: {summary.TotalManHours.ToString("F0", CultureInfo.InvariantCulture)}, Resources: {summary.ResourceCount} ({summary.RoleSummary})"))
            : "    - (No grouped task information available.)";

        IEnumerable<TimelineEstimationReference> referenceItems = references ?? Array.Empty<TimelineEstimationReference>();
        var referenceTableEntries = referenceItems
            .OrderBy(r => r.ProjectScale, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var phaseSummary = string.Join(", ", r.PhaseDurations
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}: {kv.Value}d"));
                var resourceSummary = string.Join(", ", r.ResourceAllocation
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}: {kv.Value.ToString("F1", CultureInfo.InvariantCulture)}"));
                return $"    - Scale {r.ProjectScale}: {phaseSummary} | Total: {r.TotalDurationDays}d | Resources: {resourceSummary}";
            })
            .ToList();

        var referenceTableText = referenceTableEntries.Count > 0
            ? string.Join("\n", referenceTableEntries)
            : "    - (No reference data configured. Apply conservative sequencing and buffering when estimating durations.)";

        var estimatorPhaseLines = (estimation.Phases ?? new List<TimelinePhaseEstimate>())
            .OrderBy(p => p.PhaseName, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
                $"    - {p.PhaseName}: {p.DurationDays} days ({p.SequenceType})")
            .DefaultIfEmpty("    - (No phase guidance available.)");

        var estimatorRoleLines = (estimation.Roles ?? new List<TimelineRoleEstimate>())
            .OrderBy(r => r.Role, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
                $"    - {r.Role}: {r.EstimatedHeadcount.ToString("F1", CultureInfo.InvariantCulture)} headcount (Total Man-Days: {r.TotalManDays.ToString("F1", CultureInfo.InvariantCulture)})")
            .DefaultIfEmpty("    - (No role guidance available.)");

        var estimatorSummaryLines = new List<string>
        {
            "        **TIMELINE ESTIMATOR SUMMARY:**",
            $"        - Project Scale: {estimation.ProjectScale}",
            $"        - Total Duration Target: {estimation.TotalDurationDays} days",
            $"        - Sequencing Guidance: {estimation.SequencingNotes ?? string.Empty}".TrimEnd()
        };

        estimatorSummaryLines.AddRange(estimatorPhaseLines);
        estimatorSummaryLines.Add(string.Empty);
        estimatorSummaryLines.Add("        **RESOURCE HEADCOUNT GUIDANCE:**");
        estimatorSummaryLines.AddRange(estimatorRoleLines);

        var estimatorSummaryText = string.Join(Environment.NewLine, estimatorSummaryLines);

        var allRoles = (config.Roles ?? new List<PresalesRole>())
            .Select(role => role.RoleName?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => $"\"{name}\"")
            .ToList();

        var promptBuilder = new StringBuilder();

        promptBuilder.AppendLine("You are an expert Project Management AI. Your task is to generate a detailed, day-based project schedule in a specific JSON format based on a list of tasks.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("**CRITICAL INSTRUCTIONS:**");
        promptBuilder.AppendLine("1.  **START WITH THE TIMELINE ESTIMATOR SUMMARY:** Respect the project scale, total duration target, sequencing notes, and phase durations estimated by the Timeline Estimator. The final schedule's total duration must stay aligned with the estimator's target. If you adjust a phase duration, justify it by referencing the estimator's sequencing guidance.");
        promptBuilder.AppendLine("2.  **REFERENCE HISTORICAL DATA:** Use the 'Reference Table for Duration Estimation' below to validate or refine each phase duration. When the estimator's phase duration differs from the summed activity durations, prefer the estimator guidance and explain overlaps via task start dates.");
        promptBuilder.AppendLine("3.  **UNIT IS DAYS:** The entire schedule is based on a sequence of working days (Day 1, Day 2, ...).");
        promptBuilder.AppendLine("4.  **SCHEDULING RULES:**");
        promptBuilder.AppendLine("    - Honour the estimator's sequencing: phases marked 'Serial' must not overlap; 'Subsequent' phases may have limited overlap when justified; 'Parallel' phases should overlap to reflect concurrent work.");
        promptBuilder.AppendLine("    - Tasks ('Task') within the same phase may overlap when it shortens the schedule, but the overall phase length should remain aligned with the estimator's guidance and reference durations.");
        promptBuilder.AppendLine("    - If a task requires more man-days than its duration, assume multiple team members with the same role can work in parallel. Choose a duration that reflects the headcount you assign (e.g., 6 man-days with 3 available Devs can be finished in 2 days).");
        promptBuilder.AppendLine("5.  **DURATION LOGIC:** Within each phase, set each task's `durationDays` so that the phase's total span respects the estimator duration. When necessary, use the reference table to pick realistic durations. Use `max(1, CEILING(ManDays / headcountAssignedForThatTask))` to size individual tasks, and stretch or overlap tasks as needed to fit the phase duration.");
        promptBuilder.AppendLine("6.  **RESOURCE ALLOCATION (HEADCOUNT PER DAY):**");
        promptBuilder.AppendLine("    - The `dailyEffort` array for each role must contain one number per project day (`totalDurationDays`).");
        promptBuilder.AppendLine("    - Each entry represents the number of people for that role on that day (values like 0, 0.5, 1, 2, ... are valid).");
        promptBuilder.AppendLine("    - When multiple actors share a task, divide the task's daily man-days evenly among them.");
        promptBuilder.AppendLine("    - Aim to mirror the estimator's headcount guidance. **SPECIAL RULE:** 'PM' and 'Architect' roles must appear with at least 0.5 effort from Day 1 until the project ends.");
        promptBuilder.AppendLine("7.  **JSON OUTPUT:** You MUST return ONLY a single, minified JSON object with NO commentary or explanations. The structure must be EXACTLY as follows.");
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**TIMELINE ESTIMATOR DATA:**");
        promptBuilder.AppendLine(estimatorSummaryText);
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**Reference Table for Duration Estimation:**");
        promptBuilder.AppendLine(referenceTableText);
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**PHASE SUMMARIES (match to the reference table):**");
        promptBuilder.AppendLine(phaseSummaryText);
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**TASK LIST:**");
        foreach (var taskDetail in taskDetailsForPrompt)
        {
            promptBuilder.AppendLine(taskDetail);
        }
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**ROLE LIST:**");
        promptBuilder.AppendLine($"[{string.Join(", ", allRoles)}]");
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("**JSON OUTPUT STRUCTURE:**");
        promptBuilder.AppendLine("{");
        promptBuilder.AppendLine("  \"totalDurationDays\": <number>,");
        promptBuilder.AppendLine("  \"activities\": [");
        promptBuilder.AppendLine("    {");
        promptBuilder.AppendLine("      \"activityName\": \"Project Preparation\",");
        promptBuilder.AppendLine("      \"details\": [");
        promptBuilder.AppendLine("        { \"taskName\": \"System Setup\", \"actor\": \"Architect\", \"manDays\": 0.6, \"startDay\": 1, \"durationDays\": 1 }");
        promptBuilder.AppendLine("      ]");
        promptBuilder.AppendLine("    }");
        promptBuilder.AppendLine("  ],");
        promptBuilder.AppendLine("  \"resourceAllocation\": [");
        promptBuilder.AppendLine("    { \"role\": \"Architect\", \"totalManDays\": 16.5, \"dailyEffort\": [1, 0, 1, 0, 1, ...] },");
        promptBuilder.AppendLine("    { \"role\": \"PM\", \"totalManDays\": 14.5, \"dailyEffort\": [1, 0, 1, 0, 1, ...] },");
        promptBuilder.AppendLine("    { \"role\": \"Dev\", \"totalManDays\": 26.5, \"dailyEffort\": [0, 0, 0, 0, 2, 2, 1, 0, ...] }");
        promptBuilder.AppendLine("  ]");
        promptBuilder.AppendLine("}");

        return promptBuilder.ToString();
    }
    private sealed class AiTimelineResult
    {
        public int TotalDurationDays { get; set; }
        public List<TimelineActivity> Activities { get; set; } = new();
        public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
    }
}
