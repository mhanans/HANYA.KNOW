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
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineGenerationService> _logger;

    public TimelineGenerationService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        TimelineStore timelineStore,
        TimelineEstimationStore estimationStore,
        LlmClient llmClient,
        ILogger<TimelineGenerationService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _timelineStore = timelineStore;
        _estimationStore = estimationStore;
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
        var ganttTasks = AssessmentTaskAggregator.GetGanttTasks(assessment, config);
        if (ganttTasks.Count == 0)
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

        if (estimatorRecord.Phases == null || !estimatorRecord.Phases.Any())
        {
            throw new InvalidOperationException(
                "The timeline estimator did not produce any phase guidance. Please regenerate the estimation first.");
        }

        var prompt = ConstructDailySchedulerAiPrompt(estimatorRecord, ganttTasks);
        _logger.LogInformation(
            "Requesting AI generated timeline for assessment {AssessmentId} with {TaskCount} tasks.",
            assessmentId,
            ganttTasks.Count);

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

        ValidateAiTimeline(record, config);
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
        TimelineEstimationRecord estimation,
        List<AssessmentTaskAggregator.GanttTask> ganttTasks)
    {
        if (estimation == null)
        {
            throw new ArgumentNullException(nameof(estimation));
        }

        if (estimation.TotalDurationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimation.TotalDurationDays));
        }

        if (ganttTasks == null || ganttTasks.Count == 0)
        {
            throw new InvalidOperationException("Granular tasks are required to build the AI prompt.");
        }

        static string Encode(string? value) => JsonEncodedText.Encode(value ?? string.Empty).ToString();

        var taskLines = ganttTasks.Select(t =>
            $"  - {{ \"activityGroup\": \"{Encode(t.ActivityGroup)}\", \"taskName\": \"{Encode(t.Detail)}\", \"actor\": \"{Encode(t.Actor)}\", \"manDays\": {t.ManDays.ToString("F2", CultureInfo.InvariantCulture)} }}");

        var phaseGuidanceLines = (estimation.Phases ?? new List<TimelinePhaseEstimate>())
            .Select(p => $"  - {p.PhaseName}: Target Duration = {Math.Max(1, p.DurationDays)} days, Sequencing = {p.SequenceType}")
            .ToList();

        var exampleJson = """
    {
      "totalDurationDays": 17,
      "activities": [
        { "activityName": "Project Preparation", "details": [ { "taskName": "System Setup", "actor": "Architect", "manDays": 0.6, "startDay": 1, "durationDays": 1 } ] },
        { "activityName": "Application Development", "details": [ { "taskName": "Application Development", "actor": "Dev, Dev Lead", "manDays": 10.65, "startDay": 5, "durationDays": 3 } ] }
      ],
      "resourceAllocation": [ { "role": "Architect", "totalManDays": 12.0, "dailyEffort": [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0] } ]
    }
    """;

        var resourceConstraints = @"- Architect: 1 person (Max 1.0 man-days per day)
    - Analyst: 1 person (Max 1.0 man-days per day)
    - Dev: 3 people (Max 3.0 man-days per day)
    - PM: 1 person (Max 1.0 man-days per day)
    - Dev Lead: 1 person (Max 1.0 man-days per day)";

        var taskDependencies = @"- 'SRS & FSD Documentation' must be completed before 'Application Development' can start.
    - 'Application Development' must be completed before 'SIT/Automated Testing' can start.
    - 'SIT/Automated Testing' must be completed before 'UAT' can start.
    - All development and testing activities must be completed before 'Go Live' activities can start.";

        return $@"
    You are a hyper-logical, deterministic Project Scheduling engine. Your only function is to convert a list of tasks into a valid, compact, day-by-day Gantt chart in JSON format. You must follow all rules PERFECTLY. Your output must be a single, clean JSON object.

    **1. MANDATORY CONSTRAINTS (NON-NEGOTIABLE):**

    *   **Total Project Duration:** The schedule must fit exactly within **{estimation.TotalDurationDays} days**. This is a HARD LIMIT. If necessary, increase parallelism to meet this deadline.
    *   **Resource Availability (Headcount):** The total man-days for any role on any single day CANNOT exceed the available headcount.
        {resourceConstraints}
    *   **Discrete Manpower Rule:** The daily effort for any single person must be **EXACTLY 0.5 (half-day) or 1.0 (full-day)**. No other values like 0.75, 0.29, or 0.58 are permitted. This is the most important rule.
    *   **No Gaps / Compact Scheduling:** Start every task AS EARLY AS POSSIBLE (ASAP), respecting its dependencies and resource availability. The schedule must be tightly packed with no unnecessary idle days.
    *   **Supervisory Rule:** 'Architect' and 'PM' roles, if used, MUST have a minimum effort of 0.5 allocated on every single project day to represent oversight.

    **2. HIGH-LEVEL PHASE PLAN:**
    {string.Join("\n", phaseGuidanceLines)}

    **3. TASK DEPENDENCIES:**
    {taskDependencies}

    **4. DETAILED TASK LIST TO SCHEDULE:**
    [
    {string.Join(",\n", taskLines)}
    ]

    **5. SCHEDULING LOGIC & RULES (Follow Step-by-Step):**

    **5.1. Manpower Allocation & Duration Calculation (CRITICAL RULE)**
    To comply with the Discrete Manpower Rule, you must intelligently choose the `durationDays` and the number of people assigned to a task.
    *   **Procedure:** For a given task with `manDays`, find a combination of `durationDays` and number of people that allows the work to be done in units of 0.5 or 1.0.
    *   **Example 1:** A task is 2.7 man-days for 1 Architect. A duration of 2 days is INVALID (effort would be 1.35/day). The CORRECT schedule is a `durationDays` of 3. The Architect is allocated 1.0 man-day of effort for 3 days. The total allocated effort (3.0) is slightly more than the required work (2.7), which is acceptable.
    *   **Example 2:** A task is 5.1 man-days for 'BA, Dev'. Assign 1 BA and 2 Devs (3 people total). Total daily effort is 3.0. The `durationDays` is `CEILING(5.1 / 3.0) = 2` days.
    *   The `manDays` in the output `details` array must remain the original value from the input, but your internal calculation for resource allocation must use the clean, rounded-up effort.

    **5.2. Parallelism**
    Aggressively schedule tasks in parallel whenever dependencies and resource constraints allow.

    **6. EXAMPLE OF A PERFECT SCHEDULE:**
    This example follows ALL rules. Your output structure and quality must match this example.
    ```json
    {exampleJson}
    ```

    **7. WHAT TO AVOID (INCORRECT OUTPUT):**
    The following `dailyEffort` array is an example of a FAILED output. Your generated JSON MUST NOT contain arbitrary floating-point numbers.
    - **WRONG:** `""dailyEffort"": [0.58, 0.58, 0.29, 0.54, ...]`
    - **CORRECT:** `""dailyEffort"": [1.0, 1.0, 0.5, 1.0, ...]`

    **FINAL JSON OUTPUT (Strictly this format, no commentary):**
    Now, generate the new JSON schedule for the tasks provided in Section 4. Adhere strictly to every rule.
    ";
    }

    private void ValidateAiTimeline(TimelineRecord timeline, PresalesConfiguration config)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var allocations = timeline.ResourceAllocation ?? new List<TimelineResourceAllocationEntry>();
        foreach (var allocation in allocations)
        {
            if (allocation?.DailyEffort == null)
            {
                continue;
            }

            foreach (var effort in allocation.DailyEffort)
            {
                var scaled = effort * 2;
                if (Math.Abs(scaled - Math.Round(scaled)) > 1e-6)
                {
                    throw new InvalidOperationException(
                        $"AI returned non-discrete daily effort value of {effort} for role '{allocation.Role}'. Effort must be a multiple of 0.5.");
                }
            }
        }

        var headcountLimits = BuildHeadcountLimits(config);
        if (headcountLimits.Count == 0 || timeline.TotalDurationDays <= 0)
        {
            return;
        }

        foreach (var allocation in allocations)
        {
            if (allocation?.DailyEffort == null || allocation.DailyEffort.Count == 0)
            {
                continue;
            }

            if (!TryResolveHeadcountLimit(allocation.Role, headcountLimits, out var maxHeadcount))
            {
                continue;
            }

            var daysToCheck = Math.Min(timeline.TotalDurationDays, allocation.DailyEffort.Count);
            for (var dayIndex = 0; dayIndex < daysToCheck; dayIndex++)
            {
                var required = allocation.DailyEffort[dayIndex];
                if (required - maxHeadcount > 1e-6)
                {
                    throw new InvalidOperationException(
                        $"AI over-allocated resources for role '{allocation.Role}' on day {dayIndex + 1}. Required: {required}, Available: {maxHeadcount}.");
                }
            }
        }
    }

    private static Dictionary<string, double> BuildHeadcountLimits(PresalesConfiguration config)
    {
        var limits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (config?.TeamTypes == null)
        {
            return limits;
        }

        foreach (var teamType in config.TeamTypes)
        {
            foreach (var role in teamType?.Roles ?? new List<TeamTypeRole>())
            {
                if (role == null || string.IsNullOrWhiteSpace(role.RoleName) || role.Headcount <= 0)
                {
                    continue;
                }

                if (limits.TryGetValue(role.RoleName, out var existing))
                {
                    limits[role.RoleName] = Math.Max(existing, role.Headcount);
                }
                else
                {
                    limits[role.RoleName] = role.Headcount;
                }
            }
        }

        return limits;
    }

    private static bool TryResolveHeadcountLimit(
        string role,
        IReadOnlyDictionary<string, double> headcountLimits,
        out double limit)
    {
        limit = 0;
        if (string.IsNullOrWhiteSpace(role) || headcountLimits == null || headcountLimits.Count == 0)
        {
            return false;
        }

        if (headcountLimits.TryGetValue(role, out limit))
        {
            return true;
        }

        var baseRole = PresalesRoleFormatter.ExtractBaseRole(role);
        if (!string.IsNullOrWhiteSpace(baseRole) && headcountLimits.TryGetValue(baseRole, out limit))
        {
            return true;
        }

        foreach (var kvp in headcountLimits)
        {
            if (RoleMatches(role, kvp.Key))
            {
                limit = kvp.Value;
                return true;
            }

            if (RoleMatches(kvp.Key, role))
            {
                limit = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private static bool RoleMatches(string candidate, string target)
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

    private sealed class AiTimelineResult
    {
        public int TotalDurationDays { get; set; }
        public List<TimelineActivity> Activities { get; set; } = new();
        public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
    }
}
