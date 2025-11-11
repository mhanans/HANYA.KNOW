using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        var estimationColumnEffort = AssessmentTaskAggregator.AggregateEstimationColumnEffort(assessment);
        if (estimationColumnEffort.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline estimate.");
        }

        var activityManDays = AssessmentTaskAggregator.CalculateActivityManDays(assessment, config);
        var roleManDays = AssessmentTaskAggregator.CalculateRoleManDays(assessment, config);
        var references = await _referenceStore.ListAsync(cancellationToken).ConfigureAwait(false);

        // --- ADD THIS TEMPORARY LOG ---
        _logger.LogWarning("--- AGGREGATION VERIFICATION ---");
        _logger.LogWarning($"Total Man-Days Calculated: {roleManDays.Values.Sum()}");
        foreach(var entry in roleManDays)
        {
            _logger.LogWarning($"Role: {entry.Key}, ManDays: {entry.Value}");
        }
        _logger.LogWarning("---------------------------------");
        // --- END OF LOG ---

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
            assessment,
            rawInputData.ActivityManDays,
            rawInputData.DurationsPerRole,
            rawInputData.DurationAnchor,
            rawInputData.SelectedTeamType?.Name ?? teamType.Name);
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
        if (string.IsNullOrWhiteSpace(estimation.ProjectScale))
        {
            estimation.ProjectScale = rawInputData.SelectedTeamType?.Name ?? teamType.Name;
        }

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
            estimation.SequencingNotes = "Total duration differs from summed phases due to assumed overlaps between phases.";
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
            .Where(p => !string.IsNullOrWhiteSpace(p.PhaseName))
            .Select(p => new TimelinePhaseEstimate
            {
                PhaseName = p.PhaseName.Trim(),
                DurationDays = Math.Max(1, p.DurationDays),
                SequenceType = NormaliseSequenceType(p.SequenceType)
            })
            .ToList();

        var totalDuration = Math.Max(1, result.TotalDurationDays);
        return new TimelineEstimationRecord
        {
            ProjectScale = string.IsNullOrWhiteSpace(result.ProjectScale) ? "Unknown" : result.ProjectScale.Trim(),
            TotalDurationDays = totalDuration,
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

        var phases = BuildFallbackPhases(activityManDays, reference, totalDuration);
        var roles = BuildFallbackRoles(roleManDays, totalDuration);
        var sumPhaseDurations = phases.Sum(p => p.DurationDays);
        var notes = BuildFallbackNotes(reference, totalDuration, sumPhaseDurations);

        return new TimelineEstimationRecord
        {
            ProjectScale = projectScale,
            TotalDurationDays = totalDuration,
            Phases = phases,
            Roles = roles,
            SequencingNotes = notes
        };
    }

    private static List<TimelinePhaseEstimate> BuildFallbackPhases(
        Dictionary<string, double> activityManDays,
        TimelineEstimationReference? reference,
        int totalDuration)
    {
        if (activityManDays.Count == 0)
        {
            return new List<TimelinePhaseEstimate>
            {
                new()
                {
                    PhaseName = "Overall Delivery",
                    DurationDays = totalDuration,
                    SequenceType = "Serial"
                }
            };
        }

        var totalManDays = activityManDays.Values.Sum();
        var hasOverlap = reference != null && reference.PhaseDurations.Values.Sum() > reference.TotalDurationDays;
        var orderedActivities = activityManDays
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var phases = new List<TimelinePhaseEstimate>();

        foreach (var (phaseName, manDays) in orderedActivities)
        {
            var share = totalManDays > 0 ? manDays / totalManDays : 1d / orderedActivities.Count;
            var baseline = (int)Math.Max(1, Math.Round(totalDuration * share));
            if (reference != null && reference.PhaseDurations.TryGetValue(phaseName, out var referenceDuration))
            {
                baseline = (int)Math.Max(1, Math.Round((baseline + referenceDuration) / 2d));
            }

            var sequenceType = InferSequenceType(phaseName, hasOverlap);
            phases.Add(new TimelinePhaseEstimate
            {
                PhaseName = phaseName,
                DurationDays = baseline,
                SequenceType = sequenceType
            });
        }

        return phases;
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
        int totalDuration,
        int summedPhaseDurations)
    {
        var notes = new List<string>();
        if (reference != null)
        {
            var referenceSum = reference.PhaseDurations.Values.Sum();
            if (referenceSum != reference.TotalDurationDays)
            {
                notes.Add($"Historical {reference.ProjectScale} projects show phase overlap (sum {referenceSum}d vs total {reference.TotalDurationDays}d).");
            }
        }

        if (summedPhaseDurations != totalDuration)
        {
            notes.Add($"Total duration ({totalDuration}d) differs from summed phases ({summedPhaseDurations}d) to account for parallel/subsequent work.");
        }

        if (notes.Count == 0)
        {
            notes.Add("Assuming primarily serial sequencing due to limited overlap data.");
        }

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

    private static string InferSequenceType(string phaseName, bool hasOverlap)
    {
        if (!hasOverlap)
        {
            return phaseName.Contains("test", StringComparison.OrdinalIgnoreCase)
                ? "Subsequent"
                : "Serial";
        }

        if (phaseName.Contains("deploy", StringComparison.OrdinalIgnoreCase) || phaseName.Contains("launch", StringComparison.OrdinalIgnoreCase))
        {
            return "Subsequent";
        }

        if (phaseName.Contains("plan", StringComparison.OrdinalIgnoreCase) || phaseName.Contains("prep", StringComparison.OrdinalIgnoreCase))
        {
            return "Serial";
        }

        return "Parallel";
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
        ProjectAssessment assessment,
        Dictionary<string, double> activityManDays,
        Dictionary<string, int> durationsPerRole,
        int durationAnchor,
        string teamTypeName)
    {
        _ = assessment;

        var activityLines = activityManDays
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => $"  - \"{kvp.Key}\": {kvp.Value:F1} man-days");

        var roleDurationLines = durationsPerRole
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => $"  - {kvp.Key}: Requires a minimum of {kvp.Value} working days.");

        var expectedPhases = string.Join(", ", activityManDays.Keys.Select(k => $"'{k}'"));
        var anchorRole = durationsPerRole
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();
        var anchorRoleName = string.IsNullOrWhiteSpace(anchorRole.Key)
            ? "primary role"
            : anchorRole.Key;

        var builder = new StringBuilder();
        builder.AppendLine("You are an expert Project Scheduler AI. Your task is to create a realistic project plan.");
        builder.AppendLine();
        builder.AppendLine("**Project Data:**");
        builder.AppendLine($"1.  **Team Configuration:** A '{teamTypeName}' configuration is used.");
        builder.AppendLine("2.  **Phase Effort Breakdown:** The total work required for each phase.");

        foreach (var line in activityLines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine("3.  **Resource Bottleneck Analysis:** The minimum duration required by each role. The project cannot be shorter than the longest duration listed here.");

        foreach (var line in roleDurationLines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
        builder.AppendLine("**Instructions:**");
        builder.AppendLine($"1.  **Critical Path Anchor:** The project's critical path is determined by the longest bottleneck. The final `totalDurationDays` MUST be >= **{durationAnchor} days**.");
        builder.AppendLine($"2.  **Sequence All Phases:** You MUST provide a duration and sequence for **every one of these phases:** {expectedPhases}. Do not omit any from your final JSON output.");
        builder.AppendLine("3.  **Construct Timeline:** Based on your sequence (using 'Serial', 'Subsequent', 'Parallel'), determine the final `totalDurationDays`. It will likely be longer than the anchor due to dependencies.");
        builder.AppendLine("4.  **Assign Phase Durations:** Assign a `durationDays` to each phase that is logical within your total timeline and reflects its relative effort.");
        builder.AppendLine("5.  **Output:** Provide a minified JSON response. Do not include the 'roles' array.");
        builder.AppendLine();
        builder.AppendLine("**JSON Output Example:**");
        builder.AppendLine("{");
        builder.AppendLine($"  \"projectScale\": \"{teamTypeName}\",");
        builder.AppendLine($"  \"totalDurationDays\": {durationAnchor + 15},");
        builder.AppendLine($"  \"sequencingNotes\": \"The timeline is anchored by the {anchorRoleName}'s {durationAnchor}-day work bottleneck. After sequencing all phases with overlaps, the total duration is {durationAnchor + 15} days.\",");
        builder.AppendLine("  \"phases\": [ { \"phaseName\": \"Analysis & Design\", \"durationDays\": 20, \"sequenceType\": \"Serial\" } ]");
        builder.AppendLine("}");
        builder.Append("Return ONLY the JSON object.");

        return builder.ToString();
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

    private static string NormaliseSequenceType(string? sequenceType)
    {
        if (string.IsNullOrWhiteSpace(sequenceType))
        {
            return "Serial";
        }

        var value = sequenceType.Trim();
        if (value.Equals("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            return "Parallel";
        }

        if (value.Equals("Subsequent", StringComparison.OrdinalIgnoreCase))
        {
            return "Subsequent";
        }

        return "Serial";
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
        public string SequenceType { get; set; } = "Serial";
    }

}
