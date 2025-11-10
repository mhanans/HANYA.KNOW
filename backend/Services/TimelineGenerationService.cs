using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        var aggregatedTasks = AssessmentTaskAggregator.AggregateTasks(assessment);
        if (aggregatedTasks.Count == 0)
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
        var prompt = ConstructDailySchedulerAiPrompt(aggregatedTasks, config, referenceData, estimatorRecord);
        _logger.LogInformation(
            "Requesting AI generated timeline for assessment {AssessmentId} with {TaskCount} tasks.",
            assessmentId,
            aggregatedTasks.Count);

        string rawResponse = string.Empty;
        AiTimelineResult aiTimeline;
        try
        {
            rawResponse = await _llmClient.GenerateAsync(prompt, AiProcesses.TimelineGeneration).ConfigureAwait(false);
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

        foreach (var roleName in config.Roles.Select(r => r.RoleName))
        {
            if (!string.IsNullOrWhiteSpace(roleName))
            {
                EnsureRoleLength(roleName);
            }
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

        static bool RoleMatches(string candidate, string target) =>
            !string.IsNullOrWhiteSpace(candidate) &&
            candidate.Equals(target, StringComparison.OrdinalIgnoreCase);

        foreach (var specialRole in new[] { "PM", "Architect" })
        {
            var hasRole = allocation.Keys.Any(role => RoleMatches(role, specialRole)) ||
                          config.Roles.Any(r => RoleMatches(r.RoleName, specialRole));
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
            .Select((role, index) => new { role.RoleName, index })
            .Where(x => !string.IsNullOrWhiteSpace(x.RoleName))
            .ToDictionary(x => x.RoleName, x => x.index, StringComparer.OrdinalIgnoreCase);

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
        Dictionary<string, (string DetailName, double ManDays)> tasks,
        PresalesConfiguration config,
        IReadOnlyList<TimelineEstimationReference> references,
        TimelineEstimationRecord estimation)
    {
        var taskMetadata = tasks.Select(kvp =>
        {
            var taskKey = kvp.Key;
            var manDays = kvp.Value.ManDays;
            var roles = config.TaskRoles
                .Where(tr => tr.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))
                .Select(tr => tr.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var actorString = roles.Any() ? string.Join(", ", roles) : "Unassigned";
            var activityGroup = config.TaskActivities
                .FirstOrDefault(ta => ta.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))?.ActivityName ?? "Unmapped";
            return new
            {
                TaskKey = taskKey,
                ManDays = manDays,
                Roles = roles,
                ActorString = actorString,
                ActivityGroup = activityGroup
            };
        })
        .OrderBy(t => t.ActivityGroup, StringComparer.OrdinalIgnoreCase)
        .ThenBy(t => t.TaskKey, StringComparer.OrdinalIgnoreCase)
        .ToList();

        var taskDetailsForPrompt = taskMetadata.Select(t =>
            $"  - Task: \"{t.TaskKey}\", ManDays: {t.ManDays.ToString("F2", CultureInfo.InvariantCulture)}, Actor(s): \"{t.ActorString}\", Group: \"{t.ActivityGroup}\"");

        var phaseSummaries = taskMetadata
            .GroupBy(t => t.ActivityGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var roles = group
                    .SelectMany(x => x.Roles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var resourceCount = roles.Count > 0 ? roles.Count : 1;
                var roleSummary = roles.Count > 0 ? string.Join(", ", roles) : "Unassigned";
                return new
                {
                    ActivityGroup = group.First().ActivityGroup,
                    TotalManHours = group.Sum(x => x.ManDays * 8d),
                    ResourceCount = resourceCount,
                    RoleSummary = roleSummary
                };
            })
            .OrderBy(s => s.ActivityGroup, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var phaseSummaryText = phaseSummaries.Count > 0
            ? string.Join("\n", phaseSummaries.Select(summary =>
                $"    - Group: \"{summary.ActivityGroup}\", TotalManHours: {summary.TotalManHours.ToString("F0", CultureInfo.InvariantCulture)}, Resources: {summary.ResourceCount} ({summary.RoleSummary})"))
            : "    - (No grouped task information available.)";

        var referenceTableEntries = (references ?? Array.Empty<TimelineEstimationReference>())
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

        var estimatorSummaryText = $@"
        **TIMELINE ESTIMATOR SUMMARY:**
        - Project Scale: {estimation.ProjectScale}
        - Total Duration Target: {estimation.TotalDurationDays} days
        - Sequencing Guidance: {estimation.SequencingNotes}
{string.Join("\n", estimatorPhaseLines)}

        **RESOURCE HEADCOUNT GUIDANCE:**
{string.Join("\n", estimatorRoleLines)}
        ";

        var allRoles = config.Roles.Select(r => $"\"{r.RoleName}\"").ToList();

        return $@"
        You are an expert Project Management AI. Your task is to generate a detailed, day-based project schedule in a specific JSON format based on a list of tasks.

        **CRITICAL INSTRUCTIONS:**
        1.  **START WITH THE TIMELINE ESTIMATOR SUMMARY:** Respect the project scale, total duration target, sequencing notes, and phase durations estimated by the Timeline Estimator. The final schedule's total duration must stay aligned with the estimator's target. If you adjust a phase duration, justify it by referencing the estimator's sequencing guidance.
        2.  **REFERENCE HISTORICAL DATA:** Use the 'Reference Table for Duration Estimation' below to validate or refine each phase duration. When the estimator's phase duration differs from the summed activity durations, prefer the estimator guidance and explain overlaps via task start dates.
        2.  **UNIT IS DAYS:** The entire schedule is based on a sequence of working days (Day 1, Day 2, ...).
        3.  **SCHEDULING RULES:**
            - Honour the estimator's sequencing: phases marked 'Serial' must not overlap; 'Subsequent' phases may have limited overlap when justified; 'Parallel' phases should overlap to reflect concurrent work.
            - Tasks ('Task') within the same phase may overlap when it shortens the schedule, but the overall phase length should remain aligned with the estimator's guidance and reference durations.
            - If a task requires more man-days than its duration, assume multiple team members with the same role can work in parallel. Choose a duration that reflects the headcount you assign (e.g., 6 man-days with 3 available Devs can be finished in 2 days).
        4.  **DURATION LOGIC:** Within each phase, set each task's `durationDays` so that the phase's total span respects the estimator duration. When necessary, use the reference table to pick realistic durations. Use `max(1, CEILING(ManDays / headcountAssignedForThatTask))` to size individual tasks, and stretch or overlap tasks as needed to fit the phase duration.
        5.  **RESOURCE ALLOCATION (HEADCOUNT PER DAY):**
            - The `dailyEffort` array for each role must contain one number per project day (`totalDurationDays`).
            - Each entry represents the number of people for that role on that day (values like 0, 0.5, 1, 2, ... are valid).
            - When multiple actors share a task, divide the task's daily man-days evenly among them.
            - Aim to mirror the estimator's headcount guidance. **SPECIAL RULE:** 'PM' and 'Architect' roles must appear with at least 0.5 effort from Day 1 until the project ends.
        6.  **JSON OUTPUT:** You MUST return ONLY a single, minified JSON object with NO commentary or explanations. The structure must be EXACTLY as follows.

        **TIMELINE ESTIMATOR DATA:**
{estimatorSummaryText}

        **Reference Table for Duration Estimation:**
{referenceTableText}

        **PHASE SUMMARIES (match to the reference table):**
{phaseSummaryText}

        **TASK LIST:**
        {string.Join("\n", taskDetailsForPrompt)}

        **ROLE LIST:**
        [{string.Join(", ", allRoles)}]

        **JSON OUTPUT STRUCTURE:**
        {{
          ""totalDurationDays"": <number>,
          ""activities"": [
            {{
              ""activityName"": ""Project Preparation"",
              ""details"": [
                {{ ""taskName"": ""System Setup"", ""actor"": ""Architect"", ""manDays"": 0.6, ""startDay"": 1, ""durationDays"": 1 }}
              ]
            }}
          ],
          ""resourceAllocation"": [
            {{ ""role"": ""Architect"", ""totalManDays"": 16.5, ""dailyEffort"": [1, 0, 1, 0, 1, ...] }},
            {{ ""role"": ""PM"", ""totalManDays"": 14.5, ""dailyEffort"": [1, 0, 1, 0, 1, ...] }},
            {{ ""role"": ""Dev"", ""totalManDays"": 26.5, ""dailyEffort"": [0, 0, 0, 0, 2, 2, 1, 0, ...] }}
          ]
        }}
    ";
    }

    private sealed class AiTimelineResult
    {
        public int TotalDurationDays { get; set; }
        public List<TimelineActivity> Activities { get; set; } = new();
        public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
    }
}
