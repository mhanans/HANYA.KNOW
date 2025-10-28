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
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineGenerationService> _logger;

    public TimelineGenerationService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        TimelineStore timelineStore,
        LlmClient llmClient,
        ILogger<TimelineGenerationService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _timelineStore = timelineStore;
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
        var aggregatedTasks = AggregateTasks(assessment);
        if (aggregatedTasks.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline.");
        }

        var prompt = ConstructDailySchedulerAiPrompt(aggregatedTasks, config);
        _logger.LogInformation(
            "Requesting AI generated timeline for assessment {AssessmentId} with {TaskCount} tasks.",
            assessmentId,
            aggregatedTasks.Count);

        var aiTimeline = await GetScheduleFromAiAsync(prompt).ConfigureAwait(false);

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

        await _timelineStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    private async Task<AiTimelineResult> GetScheduleFromAiAsync(string prompt)
    {
        try
        {
            var response = await _llmClient.GenerateAsync(prompt).ConfigureAwait(false);
            return ParseAiTimeline(response);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI response was not valid JSON for timeline generation.");
            throw new InvalidOperationException("AI returned an invalid timeline response.", ex);
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

    private string ConstructDailySchedulerAiPrompt(
        Dictionary<string, (string DetailName, double ManDays)> tasks,
        PresalesConfiguration config)
    {
        var taskDetailsForPrompt = tasks.Select(kvp =>
        {
            var taskKey = kvp.Key;
            var manDays = kvp.Value.ManDays;
            var roles = config.TaskRoles
                .Where(tr => tr.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))
                .Select(tr => tr.RoleName)
                .ToList();
            var actorString = roles.Any() ? string.Join(", ", roles) : "Unassigned";
            var activityGroup = config.TaskActivities
                .FirstOrDefault(ta => ta.TaskKey.Equals(taskKey, StringComparison.OrdinalIgnoreCase))?.ActivityName ?? "Unmapped";
            return $"  - Task: \"{taskKey}\", ManDays: {manDays:F2}, Actor(s): \"{actorString}\", Group: \"{activityGroup}\"";
        });

        var allRoles = config.Roles.Select(r => $"\"{r.RoleName}\"").ToList();

        return $@"
        You are an expert Project Management AI. Your task is to generate a detailed, day-based project schedule in a specific JSON format based on a list of tasks.

        **CRITICAL INSTRUCTIONS:**
        1.  **UNIT IS DAYS:** The entire schedule is based on a sequence of working days (Day 1, Day 2, ...).
        2.  **SEQUENCING:**
            - Project phases ('Group') are strictly sequential. 'Analysis & Design' must finish before 'Application Development' begins.
            - Tasks ('Task') WITHIN a phase are also sequential (waterfall).
        3.  **DURATION:** Task duration in days is `CEILING(ManDays)`.
        4.  **RESOURCE ALLOCATION (HEADCOUNT PER DAY):**
            - For each day, determine which task is active.
            - For that active task, look at its 'Actor(s)'.
            - The daily headcount for each listed actor is `1.0 / (number of actors)`. E.g., if actors are 'Dev, Dev Lead', each gets 0.5 for that day. If actor is 'BA', BA gets 1.0.
            - **SPECIAL RULE:** 'PM' and 'Architect' roles are always active from Day 1 to the final day of the project. Their daily headcount is always 0.5.
            - All other roles have a headcount of 0 on days they are not working.
            - Round the final daily headcount to the nearest whole number (0, 1, 2...).
        5.  **JSON OUTPUT:** You MUST return ONLY a single, minified JSON object with NO commentary or explanations. The structure must be EXACTLY as follows.

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

    private sealed class AiTimelineResult
    {
        public int TotalDurationDays { get; set; }
        public List<TimelineActivity> Activities { get; set; } = new();
        public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
    }
}
