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
    private readonly ProjectTemplateStore _templateStore;
    private readonly TimelineEstimationReferenceStore _referenceStore;
    private readonly TimelineEstimationStore _estimationStore;
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineEstimatorService> _logger;

    public TimelineEstimatorService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        ProjectTemplateStore templateStore,
        TimelineEstimationReferenceStore referenceStore,
        TimelineEstimationStore estimationStore,
        LlmClient llmClient,
        ILogger<TimelineEstimatorService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _templateStore = templateStore;
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
            var template = await _templateStore.GetAsync(assessment.TemplateId).ConfigureAwait(false);
            estimation = BuildDeterministicEstimate(activityManDays, roleManDays, teamType, durationAnchor, template?.TimelinePhases);
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
        int durationAnchor,
        List<TimelinePhaseTemplate>? templatePhases = null)
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
            ("Analysis & Design", "Parallel"),
            ("Architecture & Setup", "Parallel"),
            ("Application Development", "Subsequent"),
            ("Testing & QA", "Parallel"),
            ("Testing & Bug Fixing", "Parallel"),

            ("Deployment & Handover", "Subsequent")
        };

        if (templatePhases != null && templatePhases.Any())
        {
            // Use template phases to override default sequence
            phaseSequence = templatePhases.OrderBy(p => p.StartDay).Select(p => (p.Name, "Custom")).ToList();
        }

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
            // If using template phases, we might want to force them even if no specific activity map
            // For now, only include if activity matches or if it's a template phase
            var templatePhase = templatePhases?.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            
            if (!normalizedActivities.TryGetValue(name, out var manDays) || manDays <= 0)
            {
                if (templatePhase != null)
                {
                     // Force include template phase even if no direct activity mapping, assume mostly logic/overhead
                     manDays = templatePhase.Duration * averageHeadcount; // Reverse engineer effort from duration
                }
                else
                {
                    continue;
                }
            }

            var durationDays = templatePhase?.Duration ?? CalculatePhaseDuration(manDays, averageHeadcount);
            var sequence = templatePhase != null ? "Subsequent" : sequenceType; // Default to Subsequent for template logic if uncertain, or handle 'Custom' logic
            
            // Refine sequence for template phases based on StartDay relative overlapping?
            // Simple approach: Trust the loop order and set type based on StartDay diffs?
            // For Deterministic builder, we are limited.
            // Let's just use the duration.
            
            phaseEstimates.Add(new TimelinePhaseEstimate
            {
                PhaseName = name,
                DurationDays = durationDays,
                SequenceType = sequence
            });

            if (normalizedActivities.ContainsKey(name)) normalizedActivities.Remove(name);
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
    public async Task<TeamRecommendation> RecommendTeamAsync(
        int assessmentId,
        CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, null).ConfigureAwait(false);
        if (assessment == null)
        {
            throw new KeyNotFoundException($"Assessment {assessmentId} was not found.");
        }

        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var roleManDays = AssessmentTaskAggregator.CalculateRoleManDays(assessment, config);
        var totalManDays = roleManDays.Values.Sum();
        var totalManHours = totalManDays * 8; // Convention: 1 MD = 8 MH

        // User requested to treat Config Thresholds (MinManDays/MaxManDays) as Man-Hours
        var teamType = config.TeamTypes
            .OrderBy(t => t.MinManDays)
            .FirstOrDefault(t => totalManHours >= t.MinManDays && (t.MaxManDays <= 0 || totalManHours <= t.MaxManDays))
            ?? config.TeamTypes.FirstOrDefault(t => t.Name.Contains("Medium", StringComparison.OrdinalIgnoreCase))
            ?? config.TeamTypes.FirstOrDefault();

        var recommendedRoles = new List<TimelineRoleEstimate>();
        if (teamType != null)
        {
            foreach (var roleDef in teamType.Roles ?? new List<TeamTypeRole>())
            {
                if (roleManDays.TryGetValue(roleDef.RoleName, out var days))
                {
                    recommendedRoles.Add(new TimelineRoleEstimate 
                    { 
                        Role = roleDef.RoleName, 
                        EstimatedHeadcount = roleDef.Headcount,
                        TotalManDays = days 
                    });
                }
                else
                {
                     // Include even if 0 hours? User might want to see standard team structure.
                     recommendedRoles.Add(new TimelineRoleEstimate 
                    { 
                        Role = roleDef.RoleName, 
                        EstimatedHeadcount = roleDef.Headcount,
                        TotalManDays = 0 
                    });
                }
            }
            
            // Add roles that have effort but aren't in the standard team type
            foreach (var kvp in roleManDays)
            {
                if (!recommendedRoles.Any(r => string.Equals(r.Role, kvp.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    recommendedRoles.Add(new TimelineRoleEstimate 
                    { 
                        Role = kvp.Key, 
                        EstimatedHeadcount = 1, // Default to 1 if not defined in team
                        TotalManDays = kvp.Value 
                    });
                }
            }
        }

        return new TeamRecommendation
        {
            TotalManDays = totalManDays,
            TotalManHours = totalManHours,
            RecommendedTeamName = teamType?.Name ?? "Custom",
            Roles = recommendedRoles
        };
    }

    public async Task<TimelineEstimationRecord> GenerateStrictAsync(
        int assessmentId,
        List<TimelineRoleEstimate> confirmedTeam,
        CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, null).ConfigureAwait(false);
        if (assessment == null)
        {
            throw new KeyNotFoundException($"Assessment {assessmentId} was not found.");
        }

        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var activityManDays = AssessmentTaskAggregator.CalculateActivityManDays(assessment, config);
        var roleManDays = AssessmentTaskAggregator.CalculateRoleManDays(assessment, config);
        var totalManDays = roleManDays.Values.Sum();

        // Construct a virtual TeamType from the confirmed inputs
        var customTeamType = new TeamType
        {
            Name = "Custom Confirmed Team",
            Roles = confirmedTeam.Select(r => new TeamTypeRole 
            { 
                RoleName = r.Role, 
                Headcount = r.EstimatedHeadcount 
            }).ToList()
        };

        // Calculate 'DurationsPerRole' for the Anchor
        var durationsPerRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in confirmedTeam)
        {
            if (roleManDays.TryGetValue(role.Role, out var md) && role.EstimatedHeadcount > 0)
            {
                durationsPerRole[role.Role] = (int)Math.Ceiling(md / role.EstimatedHeadcount);
            }
        }

        var durationAnchor = durationsPerRole.Values.Any()
            ? durationsPerRole.Values.Max()
            : Math.Max(1, (int)Math.Ceiling(totalManDays));

        var template = await _templateStore.GetAsync(assessment.TemplateId).ConfigureAwait(false);
        
        // Use Deterministic Builder with Strict Template adherence
        var estimation = BuildDeterministicEstimate(
            activityManDays, 
            roleManDays, 
            customTeamType, 
            durationAnchor, 
            template?.TimelinePhases);

        estimation.AssessmentId = assessmentId;
        estimation.ProjectName = assessment.ProjectName;
        estimation.TemplateName = assessment.TemplateName ?? string.Empty;
        estimation.GeneratedAt = DateTime.UtcNow;
        estimation.Roles = confirmedTeam; // Persist the confirmed team
        
        // Save
        await _estimationStore.SaveAsync(estimation, cancellationToken).ConfigureAwait(false);
        return estimation;
    }

    public async Task<TimelineRecord> GenerateTimelineFromStrictAsync(
        int assessmentId,
        TimelineEstimationRecord strictEstimation,
        int bufferPercentage,
        CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, null).ConfigureAwait(false);
        if (assessment == null) throw new KeyNotFoundException($"Assessment {assessmentId} not found.");
        
        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var template = await _templateStore.GetAsync(assessment.TemplateId).ConfigureAwait(false);
        if (template == null) throw new InvalidOperationException("Project Template not found.");

        var ganttTasks = AssessmentTaskAggregator.GetGanttTasks(assessment, config);
        
        var timeline = new TimelineRecord
        {
            AssessmentId = assessmentId,
            ProjectName = assessment.ProjectName,
            TemplateName = assessment.TemplateName ?? string.Empty,
            GeneratedAt = DateTime.UtcNow,
            Activities = new List<TimelineActivity>(),
            ResourceAllocation = new List<TimelineResourceAllocationEntry>()
        };

        var phases = template.TimelinePhases ?? new List<TimelinePhaseTemplate>();
        var roleHeadcounts = strictEstimation.Roles?.ToDictionary(r => r.Role, r => r.EstimatedHeadcount, StringComparer.OrdinalIgnoreCase) 
            ?? new Dictionary<string, double>();
        
        double bufferMultiplier = 1.0 + (bufferPercentage / 100.0);

        // 1. Calculate Actual Duration for each Phase based on Workload
        var phaseMetrics = new Dictionary<string, (int TemplateStart, int TemplateDuration, int ActualDuration, int ActualStart)>(StringComparer.OrdinalIgnoreCase);
        var phaseActivities = new Dictionary<string, TimelineActivity>(StringComparer.OrdinalIgnoreCase);

        // DEBUG: Diagnose Phase Mismatch
        var groups = ganttTasks.Select(t => t.ActivityGroup).Distinct().ToList();
        _logger.LogWarning($"[PHASE DEBUG] Available Groups in GanttTasks: {string.Join(", ", groups.Select(g => $"'{g}'"))}");
        _logger.LogWarning($"[PHASE DEBUG] Template Phases: {string.Join(", ", phases.Select(p => $"'{p.Name}'"))}");

        foreach (var ph in phases)
        {
            var phaseTasks = ganttTasks.Where(t => string.Equals(t.ActivityGroup, ph.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            
            // FALLBACK: If no tasks found by explicit group name, consider ALL tasks as candidates.
            // This fixes issues where the Template Phase Name (e.g. "Development & Testing") 
            // differs from the Aggregated Group Name (e.g. "Application Development").
            if (phaseTasks.Count == 0)
            {
                phaseTasks = ganttTasks.ToList();
                _logger.LogWarning($"[PHASE RELAXED] Phase '{ph.Name}' found no direct matches. Using ALL {phaseTasks.Count} tasks as candidates.");
            }

            var activity = new TimelineActivity { ActivityName = ph.Name, Details = new List<TimelineDetail>() };
            int calculatedDuration = 0;

            if (ph.Items != null && ph.Items.Any())
            {
                // STANDARD PHASE
                int minItemStart = int.MaxValue;
                int maxItemEnd = int.MinValue;

                foreach (var tmplItem in ph.Items)
                {
                    // DEBUG: Special check for problematic task
                    bool isDebugTarget = tmplItem.Name.Contains("Sprint Planning", StringComparison.OrdinalIgnoreCase); 
                    if (isDebugTarget)
                    {
                         _logger.LogWarning($"[MATCH DEBUG] Target Template Item: '{tmplItem.Name}' in Phase '{ph.Name}'. Candidates in Phase: {phaseTasks.Count}");
                         foreach(var pt in phaseTasks)
                         {
                             bool match1 = tmplItem.Name.Contains(pt.Detail, StringComparison.OrdinalIgnoreCase);
                             bool match2 = pt.Detail.Contains(tmplItem.Name, StringComparison.OrdinalIgnoreCase);
                             _logger.LogWarning($"   - Candidate: '{pt.Detail}' (Grp: {pt.ActivityGroup}) | Match T->C: {match1} | Match C->T: {match2} | MD: {pt.ManDays}");
                         }
                    }

                    // Loose match by Name (Bidirectional Contains to catch partial AI matches)
                    var matchedTasks = phaseTasks.Where(t => 
                        !string.IsNullOrWhiteSpace(t.Detail) && (
                            tmplItem.Name.Contains(t.Detail, StringComparison.OrdinalIgnoreCase) || 
                            t.Detail.Contains(tmplItem.Name, StringComparison.OrdinalIgnoreCase)
                        )).ToList();
                    
                    double totalRawManDays = matchedTasks.Sum(t => t.ManDays);
                    double adjustedManDays = totalRawManDays * bufferMultiplier;

                    // Determine Actor
                    string primaryActor = "General";
                    double maxHc = 1;

                    if (template.EffortRoleMapping != null && template.EffortRoleMapping.TryGetValue(tmplItem.Name, out var mappedRole))
                    {
                        var candidates = mappedRole.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(s => s.Trim())
                                                   .ToList();
                        
                        // Filter candidates
                        var validActors = new List<string>();
                        foreach (var c in candidates)
                        {
                           // 1. Exact Match
                           var match = roleHeadcounts.Keys.FirstOrDefault(k => string.Equals(k, c, StringComparison.OrdinalIgnoreCase));
                           
                           // 2. Team Role Contains Candidate (e.g. Team: "Senior Business Analyst", Candidate: "Business Analyst")
                           if (match == null) match = roleHeadcounts.Keys.FirstOrDefault(k => k.Contains(c, StringComparison.OrdinalIgnoreCase));
                           
                           // 3. Candidate Contains Team Role (e.g. Team: "Analyst", Candidate: "Business Analyst")
                           if (match == null) match = roleHeadcounts.Keys.FirstOrDefault(k => c.Contains(k, StringComparison.OrdinalIgnoreCase));
                           
                           if (match != null && !validActors.Contains(match)) validActors.Add(match);
                        }

                        if (validActors.Any())
                        {
                            primaryActor = string.Join(", ", validActors);
                            maxHc = validActors.Max(a => roleHeadcounts.GetValueOrDefault(a, 1));
                        }
                        else
                        {
                            // If no role matched, default to General/1.
                            primaryActor = "General"; 
                        }
                    }
                    else if (matchedTasks.Any())
                    {
                        primaryActor = matchedTasks.Select(t => t.Actor)
                            .GroupBy(x => x)
                            .OrderByDescending(g => g.Count())
                            .FirstOrDefault()?.Key ?? "General";
                            
                         if (roleHeadcounts.ContainsKey(primaryActor)) maxHc = roleHeadcounts[primaryActor];
                         else {
                             // Fallback: Try bidirectional contains match
                             var best = roleHeadcounts.Keys.FirstOrDefault(k => primaryActor.Contains(k, StringComparison.OrdinalIgnoreCase) || k.Contains(primaryActor, StringComparison.OrdinalIgnoreCase));
                             if (best != null) { primaryActor = best; maxHc = roleHeadcounts[best]; }
                         }
                    }
                    else
                    {
                         // Fallback parser
                         var parts = tmplItem.Name.Split('-');
                         if (parts.Length > 1) primaryActor = parts.Last().Replace("Setup", "").Trim();
                         
                         if (roleHeadcounts.ContainsKey(primaryActor)) maxHc = roleHeadcounts[primaryActor];
                         else {
                             var best = roleHeadcounts.Keys.FirstOrDefault(k => primaryActor.Contains(k, StringComparison.OrdinalIgnoreCase) || k.Contains(primaryActor, StringComparison.OrdinalIgnoreCase));
                             if (best != null) { primaryActor = best; maxHc = roleHeadcounts[best]; }
                         }
                    }

                    // Enforce minimum headcount of 1 for duration calculation
                    if (maxHc < 1) maxHc = 1;

                    // Fallback: Use Template Duration as ManDays estimate if no assessment data
                    if (adjustedManDays <= 0)
                    {
                         adjustedManDays = (double)tmplItem.Duration;
                    }

                    var duration = (int)Math.Ceiling(adjustedManDays / maxHc);
                    duration = Math.Max(1, duration);

                    _logger.LogWarning($"[CRITICAL DEBUG] Task: '{tmplItem.Name}' | Actor: '{primaryActor}' | ManDays: {adjustedManDays:F4} | HC: {maxHc:F2} | FinalDuration: {duration} days");

                    int relativeStart = tmplItem.StartDayOffset; 
                    int relativeEnd = relativeStart + duration;

                    if (relativeStart < minItemStart) minItemStart = relativeStart;
                    if (relativeEnd > maxItemEnd) maxItemEnd = relativeEnd;

                    activity.Details.Add(new TimelineDetail
                    {
                        TaskName = tmplItem.Name,
                        Actor = primaryActor,
                        ManDays = adjustedManDays,
                        StartDay = relativeStart,
                        DurationDays = duration
                    });
                }
                
                calculatedDuration = (maxItemEnd > 0) ? maxItemEnd : ph.Duration;
                // Fix: If we have explicit items, the phase duration IS the span of those items.
                if (maxItemEnd > 0) calculatedDuration = maxItemEnd;
            }
            else
            {
                // DYNAMIC PHASE
                var estPhase = strictEstimation.Phases?.FirstOrDefault(p => string.Equals(p.PhaseName, ph.Name, StringComparison.OrdinalIgnoreCase));
                bool isParallel = string.Equals(estPhase?.SequenceType, "Parallel", StringComparison.OrdinalIgnoreCase);

                var groupedTasks = phaseTasks.GroupBy(t => t.Detail).ToList();
                int currentOffset = 0;
                
                foreach (var g in groupedTasks)
                {
                    double rawMd = g.Sum(x => x.ManDays);
                    double adjMd = rawMd * bufferMultiplier;
                    
                    string actor = g.First().Actor;
                    
                    if (!roleHeadcounts.ContainsKey(actor))
                    {
                         var bestMatch = roleHeadcounts.Keys.FirstOrDefault(k => actor.Contains(k, StringComparison.OrdinalIgnoreCase));
                         if (bestMatch != null) actor = bestMatch;
                    }

                    if (roleHeadcounts.TryGetValue(actor, out var hc) && hc > 0) { } else { hc = 1; }
                    
                    int dur = Math.Max(1, (int)Math.Ceiling(adjMd / hc));
                    
                    activity.Details.Add(new TimelineDetail
                    {
                        TaskName = g.Key,
                        Actor = actor,
                        ManDays = adjMd,
                        StartDay = isParallel ? 0 : currentOffset,
                        DurationDays = dur
                    });

                    if (!isParallel) currentOffset += dur;
                    else calculatedDuration = Math.Max(calculatedDuration, dur);
                }
                // If parallel, calculatedDuration is max. If serial, it's sum.
                // If parallel, currentOffset is not accumulated.
                if (!isParallel) calculatedDuration = currentOffset;
                
                if (calculatedDuration == 0) calculatedDuration = ph.Duration; 
            }

            phaseMetrics[ph.Name] = (ph.StartDay, ph.Duration, calculatedDuration, 0);
            phaseActivities[ph.Name] = activity;
        }

        // 2. Schedule Phases (Elastic CPM)
        var sortedPhases = phases.OrderBy(p => p.StartDay).ToList();
        int globalMaxDay = 0;

        foreach (var p in sortedPhases)
        {
            int actualStart = 1; 
            var predecessors = sortedPhases.Where(x => x.Name != p.Name && x.StartDay <= p.StartDay).ToList();

            foreach (var pred in predecessors)
            {
                int predTemplateEnd = pred.StartDay + pred.Duration;
                int gap = p.StartDay - predTemplateEnd;

                if (gap >= 0)
                {
                    if (phaseMetrics.TryGetValue(pred.Name, out var m))
                    {
                        int constraint = (m.ActualStart + m.ActualDuration) + gap;
                        if (constraint > actualStart) actualStart = constraint;
                    }
                }
            }
            
            var currentM = phaseMetrics[p.Name];
            phaseMetrics[p.Name] = (currentM.TemplateStart, currentM.TemplateDuration, currentM.ActualDuration, actualStart);
            
            if (phaseActivities.TryGetValue(p.Name, out var act))
            {
                foreach (var det in act.Details)
                {
                    det.StartDay = actualStart + det.StartDay;
                }
                timeline.Activities.Add(act);
            }
            
            int actualEnd = actualStart + currentM.ActualDuration;
            if (actualEnd > globalMaxDay) globalMaxDay = actualEnd;
        }

        timeline.TotalDurationDays = globalMaxDay;
        
        // 3. Resource Allocation
        timeline.ResourceAllocation = CalculateResourceAllocation(timeline.Activities, timeline.TotalDurationDays, strictEstimation.Roles);

        return timeline;
    }

    private List<TimelineResourceAllocationEntry> CalculateResourceAllocation(
        List<TimelineActivity> activities, 
        int totalDays,
        List<TimelineRoleEstimate>? confirmedRoles)
    {
        var result = new List<TimelineResourceAllocationEntry>();
        if (confirmedRoles == null || !confirmedRoles.Any()) return result;

        // Flatten all tasks to find start/end range per role
        var roleRanges = new Dictionary<string, (int MinStart, int MaxEnd)>(StringComparer.OrdinalIgnoreCase);

        foreach (var act in activities)
        {
            foreach (var det in act.Details)
            {
                if (det.DurationDays <= 0) continue;
                
                // Split actor string (e.g., "Business Analyst, Developer")
                var taskActors = det.Actor.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(s => s.Trim())
                                          .ToList();

                foreach (var actorCandidate in taskActors)
                {
                    var rName = actorCandidate;
                    var match = confirmedRoles.FirstOrDefault(r => r.Role.Equals(rName, StringComparison.OrdinalIgnoreCase)) 
                                ?? confirmedRoles.FirstOrDefault(r => rName.Contains(r.Role, StringComparison.OrdinalIgnoreCase));
                    
                    if (match != null)
                    {
                        rName = match.Role;
                        int start = det.StartDay;
                        int end = det.StartDay + det.DurationDays - 1;

                        if (!roleRanges.ContainsKey(rName)) roleRanges[rName] = (start, end);
                        else
                        {
                            var curr = roleRanges[rName];
                            roleRanges[rName] = (Math.Min(curr.MinStart, start), Math.Max(curr.MaxEnd, end));
                        }
                    }
                }
            }
        }

        foreach (var role in confirmedRoles)
        {
            var daily = new double[totalDays + 5]; // +5 buffer
            
            // Logic: PM and Architect -> Always from Day 1 to TotalDays
            bool isAlwaysPresence = role.Role.IndexOf("Project Manager", StringComparison.OrdinalIgnoreCase) >= 0 
                                 || role.Role.IndexOf("Architect", StringComparison.OrdinalIgnoreCase) >= 0
                                 || role.Role.IndexOf("PM", StringComparison.OrdinalIgnoreCase) >= 0;

            int startScan = 1;
            int endScan = totalDays;

            if (!isAlwaysPresence)
            {
                // Use detected range
                if (roleRanges.TryGetValue(role.Role, out var range))
                {
                    startScan = range.MinStart;
                    endScan = range.MaxEnd;
                }
                else
                {
                    // Role has no tasks.
                    startScan = -1; 
                }
            }
            
            // Fill
            if (startScan > 0)
            {
                for (int d = 1; d <= totalDays; d++)
                {
                    if (d >= startScan && d <= endScan)
                    {
                        daily[d - 1] = role.EstimatedHeadcount;
                    }
                }
            }

            result.Add(new TimelineResourceAllocationEntry
            {
                Role = role.Role,
                TotalManDays = daily.Take(totalDays).Sum(),
                DailyEffort = daily.Take(totalDays).ToList()
            });
        }

        return result.OrderBy(r => r.Role).ToList();
    }

    private string? ParseActorFrom(string name)
    {
        // "System Setup - Architect Setup" -> Architect
        var parts = name.Split('-');
        if(parts.Length > 1) return parts.Last().Replace("Setup", "").Trim();
        return null;
    }

    public class TeamRecommendation
    {
        public double TotalManDays { get; set; }
        public double TotalManHours { get; set; }
        public string RecommendedTeamName { get; set; } = string.Empty;
        public List<TimelineRoleEstimate> Roles { get; set; } = new();
    }
}
