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

        TimelineEstimationRecord estimation;
        try
        {
            estimation = BuildDeterministicEstimate(activityManDays, roleManDays, teamType, durationAnchor);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deterministic timeline estimation failed. Falling back to heuristic estimation.");
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

        var phases = (result.Phases ?? new List<AiPhaseEstimate>())
            .Where(phase => !string.IsNullOrWhiteSpace(phase?.PhaseName))
            .Select(phase => new TimelinePhaseEstimate
            {
                PhaseName = phase!.PhaseName.Trim(),
                DurationDays = Math.Max(1, phase.DurationDays),
                SequenceType = NormaliseSequenceType(phase.SequenceType)
            })
            .ToList();

        return new TimelineEstimationRecord
        {
            ProjectScale = result.ProjectScale?.Trim() ?? string.Empty,
            TotalDurationDays = Math.Max(1, result.TotalDurationDays),
            Phases = phases,
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

    private TimelineEstimationRecord BuildDeterministicEstimate(
        Dictionary<string, double> activityManDays,
        Dictionary<string, double> roleManDays,
        TeamType teamType,
        int durationAnchor)
    {
        if (teamType == null)
        {
            throw new ArgumentNullException(nameof(teamType));
        }

        var normalizedActivities = new Dictionary<string, double>(activityManDays ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);
        if (normalizedActivities.Count == 0)
        {
            throw new InvalidOperationException("Deterministic estimation requires phase activity data.");
        }

        roleManDays ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        durationAnchor = Math.Max(1, durationAnchor);

        var phaseSequence = new List<(string Name, string SequenceType)>
        {
            ("Project Preparation", "Serial"),
            ("Analysis & Design", "Subsequent"),
            ("Architecture & Setup", "Subsequent"),
            ("Application Development", "Subsequent"),
            ("Testing & QA", "Parallel"),
            ("Testing & Bug Fixing", "Parallel"),
            ("Deployment & Handover", "Subsequent")
        };

        var roleDefinitions = teamType.Roles ?? new List<TeamTypeRole>();
        var validHeadcounts = roleDefinitions
            .Where(r => r != null && double.IsFinite(r.Headcount) && r.Headcount > 0)
            .Select(r => r.Headcount)
            .ToList();
        var fallbackHeadcount = roleManDays.Count > 0
            ? Math.Max(1d, roleManDays.Values.Sum() / durationAnchor)
            : 1d;
        var averageHeadcount = validHeadcounts.Count > 0 ? validHeadcounts.Average() : fallbackHeadcount;
        averageHeadcount = Math.Max(0.5, averageHeadcount);

        var phaseEstimates = new List<TimelinePhaseEstimate>();

        foreach (var (name, sequenceType) in phaseSequence)
        {
            if (!normalizedActivities.TryGetValue(name, out var manDays) || manDays <= 0)
            {
                continue;
            }

            var durationDays = CalculatePhaseDuration(manDays, averageHeadcount);
            phaseEstimates.Add(new TimelinePhaseEstimate
            {
                PhaseName = name,
                DurationDays = durationDays,
                SequenceType = sequenceType
            });

            normalizedActivities.Remove(name);
        }

        foreach (var kvp in normalizedActivities.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kvp.Value <= 0)
            {
                continue;
            }

            var durationDays = CalculatePhaseDuration(kvp.Value, averageHeadcount);
            phaseEstimates.Add(new TimelinePhaseEstimate
            {
                PhaseName = kvp.Key,
                DurationDays = durationDays,
                SequenceType = "Subsequent"
            });
        }

        if (phaseEstimates.Count == 0)
        {
            throw new InvalidOperationException("Deterministic estimation could not derive any phases.");
        }

        double totalDuration = 0;
        double parallelBlockDuration = 0;
        foreach (var phase in phaseEstimates)
        {
            phase.SequenceType = NormaliseSequenceType(phase.SequenceType);
            if (string.Equals(phase.SequenceType, "Parallel", StringComparison.OrdinalIgnoreCase))
            {
                parallelBlockDuration = Math.Max(parallelBlockDuration, phase.DurationDays);
            }
            else
            {
                totalDuration += parallelBlockDuration;
                parallelBlockDuration = 0;
                totalDuration += phase.DurationDays;
            }
        }

        totalDuration += parallelBlockDuration;
        var finalDuration = Math.Max(durationAnchor, (int)Math.Ceiling(totalDuration));
        finalDuration = Math.Max(1, finalDuration);

        var roles = BuildFallbackRoles(roleManDays, finalDuration);
        var notes = $"Phase ordering follows the presales_activities reference table, allowing Testing to run alongside Development where possible. The critical path is estimated at {finalDuration} days and is anchored by a {durationAnchor}-day resource bottleneck.";

        return new TimelineEstimationRecord
        {
            ProjectScale = teamType.Name ?? string.Empty,
            TotalDurationDays = finalDuration,
            Phases = phaseEstimates,
            Roles = roles,
            SequencingNotes = notes
        };

        int CalculatePhaseDuration(double manDays, double headcount)
        {
            var normalizedEffort = Math.Max(1d, manDays);
            var rawDuration = normalizedEffort / Math.Max(0.5, headcount);
            var duration = (int)Math.Ceiling(rawDuration);
            return Math.Max(3, duration);
        }
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
        Dictionary<string, int> durationsPerRole,
        Dictionary<string, double> activityManDays)
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
        var orderedActivities = (activityManDays ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase))
            .Where(kvp => kvp.Value > 0 && !string.IsNullOrWhiteSpace(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedActivities.Count == 0)
        {
            orderedActivities.Add(new KeyValuePair<string, double>("Project Execution", Math.Max(1, durationAnchor / 2d)));
        }

        var activityLines = orderedActivities
            .Select(kvp => $"  - \"{kvp.Key}\": {kvp.Value:F1} man-days");

        var bufferedDuration = durationAnchor + Math.Max(10, (int)Math.Round(durationAnchor * 0.25));

        return $@"
You are an expert Project Planner AI. Your task is to create a high-level phase plan based on effort and resource constraints.

**Project Data & Constraints:**
1.  **Team Configuration:** A '{teamTypeName}' is assigned.
2.  **Resource Bottleneck (Anchor):** The project's critical path is driven by the **{bottleneckRole.Key} requiring a minimum of {durationAnchor} days**. The final `totalDurationDays` must be >= this anchor.
3.  **Phase Effort Breakdown:** This is the work required for each major project phase.
{string.Join("\n", activityLines)}

**Your Task:**
1.  **Create a High-Level Schedule:** For each phase in the 'Phase Effort Breakdown', assign a realistic `durationDays` and a `sequenceType` ('Serial', 'Subsequent', 'Parallel').
2.  **Determine Total Duration:** Based on your sequencing of the phases, calculate the final `totalDurationDays` for the entire project. This will likely be longer than the {durationAnchor}-day anchor due to dependencies.
3.  **Explain Your Logic:** In `sequencingNotes`, explain how you derived the final duration from the bottleneck and phase sequencing.

**Output ONLY a minified JSON object with this exact structure:**
{{
  ""projectScale"": ""{teamTypeName}"",
  ""totalDurationDays"": {bufferedDuration},
  ""sequencingNotes"": ""The timeline is driven by the {bottleneckRole.Key}'s {durationAnchor}-day bottleneck. After sequencing all major phases with logical overlaps (e.g., Development and Testing), the total estimated duration is {bufferedDuration} days."",
  ""phases"": [
    {{ ""phaseName"": ""Project Preparation"", ""durationDays"": 5, ""sequenceType"": ""Serial"" }},
    {{ ""phaseName"": ""Development"", ""durationDays"": 40, ""sequenceType"": ""Subsequent"" }}
  ]
}}
";
    }

    private static string NormaliseSequenceType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Serial";
        }

        var value = raw.Trim();
        if (value.Equals("Parallel", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Concurrent", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Simultaneous", StringComparison.OrdinalIgnoreCase))
        {
            return "Parallel";
        }

        if (value.Equals("Subsequent", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Overlap", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Overlapping", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Stacked", StringComparison.OrdinalIgnoreCase))
        {
            return "Subsequent";
        }

        return "Serial";
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
        [JsonPropertyName("projectScale")]
        public string ProjectScale { get; set; } = string.Empty;

        [JsonPropertyName("totalDurationDays")]
        public int TotalDurationDays { get; set; }

        [JsonPropertyName("sequencingNotes")]
        public string SequencingNotes { get; set; } = string.Empty;

        [JsonPropertyName("phases")]
        public List<AiPhaseEstimate> Phases { get; set; } = new();
    }

    private sealed class AiPhaseEstimate
    {
        [JsonPropertyName("phaseName")]
        public string PhaseName { get; set; } = string.Empty;

        [JsonPropertyName("durationDays")]
        public int DurationDays { get; set; }

        [JsonPropertyName("sequenceType")]
        public string SequenceType { get; set; } = string.Empty;
    }
}
