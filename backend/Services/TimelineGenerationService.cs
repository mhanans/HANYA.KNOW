using System;
using System.Collections.Generic;
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
        var estimationColumnEffort = AssessmentTaskAggregator.AggregateEstimationColumnEffort(assessment);

        var detailedTasks = new List<DetailedTaskDescriptor>();
        var columnRoleLookup = (config.EstimationColumnRoles ?? new List<EstimationColumnRoleMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.EstimationColumn) &&
                !string.IsNullOrWhiteSpace(mapping.RoleName))
            .ToLookup(
                mapping => mapping.EstimationColumn!.Trim(),
                mapping => mapping.RoleName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections ?? new List<AssessmentSection>())
        {
            if (section == null)
            {
                continue;
            }

            foreach (var item in section.Items ?? new List<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded || item.Estimates == null)
                {
                    continue;
                }

                foreach (var estimate in item.Estimates)
                {
                    var columnName = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        continue;
                    }

                    if (!AssessmentTaskAggregator.TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    var manDays = hours / 8.0;
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    var activityGroup = AssessmentTaskAggregator.ResolveActivityName(
                        section.SectionName ?? string.Empty,
                        item.ItemName,
                        columnName,
                        config);

                    var roles = columnRoleLookup[columnName]
                        .Where(role => !string.IsNullOrWhiteSpace(role))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var actor = roles.Count > 0 ? string.Join(", ", roles) : "Unassigned";

                    detailedTasks.Add(new DetailedTaskDescriptor(
                        activityGroup,
                        item.ItemName?.Trim() ?? string.Empty,
                        columnName,
                        actor,
                        manDays));
                }
            }
        }

        if (estimationColumnEffort.Count == 0 || detailedTasks.Count == 0)
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

        var prompt = ConstructGanttChartAiPrompt(estimatorRecord, detailedTasks);
        _logger.LogInformation(
            "Requesting AI generated timeline for assessment {AssessmentId} with {TaskCount} tasks.",
            assessmentId,
            detailedTasks.Count);

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

    private string ConstructGanttChartAiPrompt(
        TimelineEstimationRecord estimation,
        IReadOnlyList<DetailedTaskDescriptor> detailedTasks)
    {
        if (estimation == null)
        {
            throw new ArgumentNullException(nameof(estimation));
        }

        if (detailedTasks == null || detailedTasks.Count == 0)
        {
            throw new InvalidOperationException("Detailed tasks are required to build the AI prompt.");
        }

        static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        var taskLines = detailedTasks.Select(task =>
            $"  - {{ \"activityGroup\": \"{Escape(task.ActivityGroup)}\", \"taskName\": \"{Escape(task.TaskName)}\", \"detail\": \"{Escape(task.Detail)}\", \"actor\": \"{Escape(task.Actor)}\", \"manDays\": {task.ManDays:F2} }}");

        var phases = (estimation.Phases ?? new List<TimelinePhaseEstimate>()).ToList();
        var phaseGuidanceLines = phases.Count > 0
            ? string.Join("\n", phases.Select(p =>
                $"  - {p.PhaseName}: Target Duration = {p.DurationDays} days, Sequencing = {p.SequenceType}"))
            : "  - No specific phase guidance provided.";

        var tasksBlock = string.Join(",\n", taskLines);

        return $@"\nYou are an expert Project Manager AI that creates detailed Gantt charts. Your task is to schedule a specific list of tasks into a daily timeline, respecting high-level guidance.\n\n**High-Level Guidance (Constraints):**\n- **Total Duration Target:** The entire project should be close to **{estimation.TotalDurationDays} days**.\n- **Phase Durations & Sequencing:** Follow this plan. 'Serial' phases must not overlap. 'Subsequent' and 'Parallel' phases can overlap.\n{phaseGuidanceLines}\n\n**Detailed Task List (You MUST schedule every task below):**\n[\n{tasksBlock}\n]\n\n**Scheduling Rules (Follow Exactly):**\n1.  **Use Real Task Names:** Your output `taskName` MUST be the `taskName` from the input list (e.g., \"System Setup\", \"Login Flow Development\"). Your `detail` field should be the specific work type (e.g., \"Architect Setup\", \"FE Development\").\n2.  **Integer Durations:** `durationDays` must be a whole number (>= 1).\n3.  **Man-day Logic:**\n    - A task's `durationDays` MUST be >= its `manDays`.\n    - If `manDays` is 0.5, `durationDays` MUST be 1 (representing a half-day of work).\n    - If `manDays` is 3 and you assign `durationDays` of 1, it implies 3 people are working on it.\n    - If `manDays` is 3 and you assign `durationDays` of 3, it implies 1 person is working on it.\n4.  **Fit to Phase:** Arrange the `startDay` and `durationDays` for each task so that the overall span of an `activityGroup` roughly matches the target duration from the high-level guidance. Use overlaps to achieve this.\n5.  **Resource Allocation:**\n    - For `resourceAllocation`, only include roles that are actually used (have `totalManDays` > 0).\n    - The `dailyEffort` array MUST have a length equal to `totalDurationDays`.\n    - Calculate daily effort precisely. Example: If a 3 man-day task is done over 2 days, the `dailyEffort` for that role is `[1.5, 1.5]` on those days.\n    - **SPECIAL RULE:** The 'Architect' and 'Project Manager' roles must have a minimum `dailyEffort` of 0.5 on every single day of the project, from day 1 to `totalDurationDays`.\n\n**Final JSON Output (Strictly this format, no extra text):**\n{{\n  \"\"totalDurationDays\"\": {estimation.TotalDurationDays},\n  \"\"activities\"\": [\n    {{\n      \"\"activityName\"\": \"\"Project Preparation\"\",\n      \"\"details\"\": [\n        {{ \"\"taskName\"\": \"\"System Setup\"\", \"\"detail\"\": \"\"Architect Setup\"\", \"\"actor\"\": \"\"Architect\"\", \"\"manDays\"\": 2.0, \"\"startDay\"\": 1, \"\"durationDays\"\": 2 }}\n      ]\n    }}\n  ],\n  \"\"resourceAllocation\"\": [\n    {{ \"\"role\"\": \"\"Architect\"\", \"\"totalManDays\"\": 25.5, \"\"dailyEffort\"\": [0.5, 0.5, 1.0, ...] }}\n  ]\n}}\n";
    }

    private sealed record DetailedTaskDescriptor(
        string ActivityGroup,
        string TaskName,
        string Detail,
        string Actor,
        double ManDays);

    private sealed class AiTimelineResult
    {
        public int TotalDurationDays { get; set; }
        public List<TimelineActivity> Activities { get; set; } = new();
        public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
    }
}
