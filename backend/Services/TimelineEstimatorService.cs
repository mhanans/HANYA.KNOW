using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class TimelineEstimatorService
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly PresalesConfigurationStore _configurationStore;
    private readonly TimelineEstimationReferenceStore _referenceStore;
    private readonly TimelineEstimationStore _estimationStore;
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineEstimatorService> _logger;

    public TimelineEstimatorService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        TimelineEstimationReferenceStore referenceStore,
        TimelineEstimationStore estimationStore,
        LlmClient llmClient,
        ILogger<TimelineEstimatorService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _referenceStore = referenceStore;
        _estimationStore = estimationStore;
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<TimelineEstimationRecord> GenerateAsync(
        int assessmentId,
        int? userId,
        CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, userId).ConfigureAwait(false);
        if (assessment == null)
        {
            throw new KeyNotFoundException($"Assessment {assessmentId} was not found.");
        }

        if (!string.Equals(assessment.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Timeline estimation requires a completed assessment.");
        }

        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var activityManDays = AssessmentTaskAggregator.CalculateActivityManDays(assessment, config);
        var roleManDays = AssessmentTaskAggregator.CalculateRoleManDays(assessment, config);
        if (roleManDays.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline estimate.");
        }

        var references = await _referenceStore.ListAsync(cancellationToken).ConfigureAwait(false);

        var totalManDays = roleManDays.Values.Sum();
        var teamType = config.TeamTypes
            .OrderBy(t => t.MinManDays)
            .FirstOrDefault(t => totalManDays >= t.MinManDays && (t.MaxManDays <= 0 || totalManDays <= t.MaxManDays))
            ?? config.TeamTypes.FirstOrDefault(t => t.Name.Contains("Medium", StringComparison.OrdinalIgnoreCase))
            ?? config.TeamTypes.FirstOrDefault();

        if (teamType == null)
        {
            throw new InvalidOperationException("No suitable team type configuration found for this project scale.");
        }

        var durationsPerRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, manDays) in roleManDays)
        {
            if (manDays <= 0)
            {
                continue;
            }

            var roleConfig = teamType.Roles.FirstOrDefault(r => string.Equals(r.RoleName, role, StringComparison.OrdinalIgnoreCase));
            var headcount = roleConfig?.Headcount ?? 1d;
            if (!double.IsFinite(headcount) || headcount <= 0)
            {
                headcount = 1d;
            }

            var duration = (int)Math.Ceiling(manDays / headcount);
            durationsPerRole[role] = Math.Max(1, duration);
        }

        var durationAnchor = durationsPerRole.Values.Any()
            ? durationsPerRole.Values.Max()
            : Math.Max(1, (int)Math.Ceiling(totalManDays));

        var rawInputData = new TimelineEstimatorRawInput
        {
            ActivityManDays = new Dictionary<string, double>(activityManDays, StringComparer.OrdinalIgnoreCase),
            RoleManDays = new Dictionary<string, double>(roleManDays, StringComparer.OrdinalIgnoreCase),
            TotalRoleManDays = totalManDays,
            DurationsPerRole = durationsPerRole,
            SelectedTeamType = CloneTeamType(teamType),
            DurationAnchor = durationAnchor
        };

        var prompt = BuildEstimatorPrompt(
            rawInputData.DurationAnchor,
            rawInputData.SelectedTeamType?.Name ?? teamType.Name,
            rawInputData.DurationsPerRole);
        string rawResponse = string.Empty;
        TimelineEstimationRecord estimation;

        try
        {
            rawResponse = await _llmClient.GenerateAsync(prompt).ConfigureAwait(false);
            var parsed = ParseAiEstimation(rawResponse);
            estimation = MapFromAiResult(parsed);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI response for timeline estimation was not valid JSON. Falling back to heuristic estimation.");
            estimation = BuildFallbackEstimate(activityManDays, roleManDays, references);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "AI timeline estimation could not be parsed. Falling back to heuristic estimation.");
            estimation = BuildFallbackEstimate(activityManDays, roleManDays, references);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI timeline estimation failed. Falling back to heuristic estimation.");
            estimation = BuildFallbackEstimate(activityManDays, roleManDays, references);
        }

        estimation.RawInputData = rawInputData;
        estimation.ProjectScale = string.IsNullOrWhiteSpace(estimation.ProjectScale)
            ? rawInputData.SelectedTeamType?.Name ?? teamType.Name
            : estimation.ProjectScale;

        if (estimation.Roles == null || !estimation.Roles.Any())
        {
            var calculatedRoles = new List<TimelineRoleEstimate>();
            if (estimation.TotalDurationDays > 0)
            {
                foreach (var (role, manDays) in roleManDays)
                {
                    if (manDays <= 0)
                    {
                        continue;
                    }

                    var headcount = manDays / estimation.TotalDurationDays;
                    var realisticHeadcount = Math.Round(headcount, 1);
                    if (realisticHeadcount == 0 && headcount > 0)
                    {
                        realisticHeadcount = 0.1;
                    }

                    calculatedRoles.Add(new TimelineRoleEstimate
                    {
                        Role = role,
                        TotalManDays = Math.Round(manDays, 2),
                        EstimatedHeadcount = realisticHeadcount
                    });
                }
            }

            estimation.Roles = calculatedRoles
                .OrderBy(r => r.Role, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        estimation.AssessmentId = assessmentId;
        estimation.ProjectName = assessment.ProjectName;
        estimation.TemplateName = assessment.TemplateName ?? string.Empty;
        estimation.GeneratedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(estimation.SequencingNotes))
        {
            estimation.SequencingNotes = "Total duration is anchored on the longest role bottleneck plus setup, overlap, and closing buffers.";
        }

        await _estimationStore.SaveAsync(estimation, cancellationToken).ConfigureAwait(false);
        return estimation;
    }

    private TimelineEstimationRecord MapFromAiResult(AiTimelineEstimationResult result)
    {
        if (result == null)
        {
            throw new InvalidOperationException("AI response did not contain an estimation result.");
        }

        var totalDuration = Math.Max(1, result.TotalDurationDays);
        return new TimelineEstimationRecord
        {
            ProjectScale = string.Empty,
            TotalDurationDays = totalDuration,
            Phases = new List<TimelinePhaseEstimate>(),
            Roles = new List<TimelineRoleEstimate>(),
            SequencingNotes = result.SequencingNotes?.Trim() ?? string.Empty
        };
    }

    private TimelineEstimationRecord BuildFallbackEstimate(
        Dictionary<string, double> activityManDays,
        Dictionary<string, double> roleManDays,
        IReadOnlyList<TimelineEstimationReference> references)
    {
        var totalManDays = activityManDays.Values.Sum();
        var projectScale = DetermineProjectScale(totalManDays, references);
        var reference = references
            .FirstOrDefault(r => string.Equals(r.ProjectScale, projectScale, StringComparison.OrdinalIgnoreCase))
            ?? references.FirstOrDefault();

        int totalDuration;
        if (reference != null && reference.TotalDurationDays > 0)
        {
            var referencePhaseTotal = reference.PhaseDurations.Values.Sum();
            var ratio = referencePhaseTotal > 0 ? totalManDays / referencePhaseTotal : 1;
            ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.6, 1.6) : 1;
            totalDuration = Math.Max(1, (int)Math.Round(reference.TotalDurationDays * ratio));
        }
        else
        {
            totalDuration = Math.Max(1, (int)Math.Ceiling(totalManDays));
        }

        var roles = BuildFallbackRoles(roleManDays, totalDuration);
        var notes = BuildFallbackNotes(reference, totalDuration);

        return new TimelineEstimationRecord
        {
            ProjectScale = projectScale,
            TotalDurationDays = totalDuration,
            Phases = new List<TimelinePhaseEstimate>(),
            Roles = roles,
            SequencingNotes = notes
        };
    }

    private static List<TimelineRoleEstimate> BuildFallbackRoles(
        Dictionary<string, double> roleManDays,
        int totalDuration)
    {
        var roles = new List<TimelineRoleEstimate>();
        foreach (var (role, manDays) in roleManDays.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (manDays <= 0)
            {
                continue;
            }

            var headcount = totalDuration > 0 ? manDays / totalDuration : manDays;
            if (headcount > 0 && headcount < 0.5)
            {
                headcount = 0.5;
            }

            roles.Add(new TimelineRoleEstimate
            {
                Role = role,
                EstimatedHeadcount = Math.Round(headcount, 2),
                TotalManDays = Math.Round(manDays, 2)
            });
        }

        if (roles.Count == 0)
        {
            roles.Add(new TimelineRoleEstimate
            {
                Role = "General",
                EstimatedHeadcount = Math.Max(1, totalDuration / 10d),
                TotalManDays = Math.Round(Math.Max(1, totalDuration * 1.5), 2)
            });
        }

        return roles;
    }

    private static string BuildFallbackNotes(
        TimelineEstimationReference? reference,
        int totalDuration)
    {
        var notes = new List<string>();
        if (reference != null)
        {
            notes.Add($"Aligned with historical {reference.ProjectScale} projects (baseline {reference.TotalDurationDays}d).");
        }

        notes.Add($"Includes buffer for setup and closing around the {totalDuration}d execution window.");

        return string.Join(" ", notes);
    }

    private static string DetermineProjectScale(
        double totalManDays,
        IReadOnlyList<TimelineEstimationReference> references)
    {
        if (references.Count > 0)
        {
            var ordered = references
                .GroupBy(r => r.ProjectScale, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Scale = group.Key,
                    AverageDuration = group.Average(r => r.TotalDurationDays)
                })
                .OrderBy(entry => entry.AverageDuration)
                .ToList();

            if (ordered.Count > 0)
            {
                var normalized = totalManDays <= 0 ? ordered.First() : ordered.LastOrDefault(e => totalManDays >= e.AverageDuration) ?? ordered.Last();
                return normalized.Scale ?? "Unknown";
            }
        }

        if (totalManDays <= 60)
        {
            return "Short";
        }

        if (totalManDays <= 120)
        {
            return "Medium";
        }

        return "Long";
    }

    private static TeamType? CloneTeamType(TeamType? source)
    {
        if (source == null)
        {
            return null;
        }

        var clone = new TeamType
        {
            Id = source.Id,
            Name = source.Name,
            MinManDays = source.MinManDays,
            MaxManDays = source.MaxManDays,
            Roles = new List<TeamTypeRole>()
        };

        foreach (var role in source.Roles ?? Enumerable.Empty<TeamTypeRole>())
        {
            if (role == null)
            {
                continue;
            }

            clone.Roles.Add(new TeamTypeRole
            {
                Id = role.Id,
                TeamTypeId = role.TeamTypeId,
                RoleName = role.RoleName,
                Headcount = role.Headcount
            });
        }

        return clone;
    }

    private string BuildEstimatorPrompt(
        int durationAnchor,
        string teamTypeName,
        Dictionary<string, int> durationsPerRole)
    {
        var orderedDurations = (durationsPerRole ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        if (orderedDurations.Count == 0)
        {
            orderedDurations.Add(new KeyValuePair<string, int>(teamTypeName ?? "Core Team", Math.Max(1, durationAnchor)));
        }

        var bottleneckRole = orderedDurations.First();
        var bufferedDuration = durationAnchor + Math.Max(1, (int)Math.Round(durationAnchor * 0.2));

        return $@"
You are a project manager AI. Your task is to provide a single, realistic total project duration.

**Constraints:**
- The project is for a '{teamTypeName}'.
- The critical path bottleneck is the **{bottleneckRole.Key}, requiring a minimum of {durationAnchor} days**.
- A real-world project requires extra time for setup, handovers, and dependencies.

**Task:**
Based on the {durationAnchor}-day bottleneck, calculate a final `totalDurationDays` that includes a realistic buffer (e.g., 15-25%) for project overhead.

**Output ONLY a minified JSON object with this exact structure:**
{{
  ""totalDurationDays"": {bufferedDuration},
  ""sequencingNotes"": ""The timeline is driven by the {bottleneckRole.Key}'s {durationAnchor}-day work bottleneck. A total duration of {bufferedDuration} days is estimated to include buffers for project overhead and phase dependencies.""
}}
";
    }
    private static AiTimelineEstimationResult ParseAiEstimation(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("AI returned an empty estimation response.");
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
                var closingFenceIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFenceIndex >= 0)
                {
                    trimmed = trimmed[..closingFenceIndex];
                }
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        var result = JsonSerializer.Deserialize<AiTimelineEstimationResult>(trimmed, options);
        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize AI estimation result.");
        }

        return result;
    }

    private sealed class AiTimelineEstimationResult
    {
        [JsonPropertyName("totalDurationDays")]
        public int TotalDurationDays { get; set; }

        [JsonPropertyName("sequencingNotes")]
        public string SequencingNotes { get; set; } = string.Empty;
    }
}
