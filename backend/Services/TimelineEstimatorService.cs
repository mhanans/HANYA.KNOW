using System;
using System.Collections.Generic;
using System.Globalization;
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
        var estimationColumnEffort = AssessmentTaskAggregator.AggregateEstimationColumnEffort(assessment);
        if (estimationColumnEffort.Count == 0)
        {
            throw new InvalidOperationException("Assessment does not contain any estimation data to generate a timeline estimate.");
        }

        var activityManDays = AssessmentTaskAggregator.CalculateActivityManDays(assessment, config);
        var roleManDays = AssessmentTaskAggregator.CalculateRoleManDays(assessment, config);
        var totalRoleManDays = roleManDays.Values.Sum();
        const double targetTeamSizeFte = 4d;
        var idealDuration = targetTeamSizeFte > 0
            ? (int)Math.Max(1, Math.Round(totalRoleManDays / targetTeamSizeFte))
            : Math.Max(1, (int)Math.Round(totalRoleManDays));
        var references = await _referenceStore.ListAsync(cancellationToken).ConfigureAwait(false);

        var prompt = BuildEstimatorPrompt(
            assessment,
            activityManDays,
            roleManDays,
            config,
            idealDuration);
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

    private string BuildEstimatorPrompt(
        ProjectAssessment assessment,
        Dictionary<string, double> activityManDays,
        Dictionary<string, double> roleManDays,
        PresalesConfiguration config,
        int idealDuration)
    {
        var activityOrderLookup = (config.Activities ?? new List<PresalesActivity>())
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ActivityName))
            .Select(activity => new
            {
                Name = activity.ActivityName.Trim(),
                Order = activity.DisplayOrder
            })
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.Order), StringComparer.OrdinalIgnoreCase);

        var activityLines = activityManDays
            .OrderBy(kvp => activityOrderLookup.TryGetValue(kvp.Key, out var order) ? order : int.MaxValue)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
                $"  - Phase: \"{kvp.Key}\" => {kvp.Value.ToString("F2", CultureInfo.InvariantCulture)} man-days");
        var totalManDays = roleManDays.Values.Sum();

        return $@"
You are a methodical project planning AI. Your task is to create a high-level project timeline based on effort estimates.

**Inputs:**
- Project: {assessment.ProjectName}
- Total Man-Days: {totalManDays.ToString("F2", CultureInfo.InvariantCulture)}
- Phase Effort Summary (man-days per logical phase):
{string.Join("\n", activityLines)}

**Guidance & Constraints:**
1.  **Primary Anchor:** Based on the total effort, a project of this scale with a standard team size suggests an ideal duration of approximately **{idealDuration} working days**. Your final `totalDurationDays` MUST be very close to this anchor. Do not deviate significantly.
2.  **Logical Sequencing:** A typical project flow is: Preparation -> Design -> Architecture -> Development -> Testing -> Deployment -> Closing. Use this as a guide for your 'Serial' and 'Subsequent' sequencing. 'Development' and 'Testing' can have some parallel overlap.

**Requirements:**
1.  Determine the project `projectScale`.
2.  Estimate a final `totalDurationDays`, using the **{idealDuration} day anchor** as your primary guide.
3.  For each input phase, assign a `durationDays` and a `sequenceType` ('Serial', 'Subsequent', 'Parallel').
4.  **Self-Validation:** Before finalizing your JSON, mentally calculate the timeline based on your phase durations and sequences. For example, `Serial(10) + Parallel(30, 20) + Subsequent(5)` results in a timeline of roughly `10 + 30 + 5 = 45` days. Ensure your `totalDurationDays` is consistent with this calculation from your own phase estimates.
5.  In `sequencingNotes`, explain how overlaps affect the timeline.
6.  Output strictly valid minified JSON. DO NOT include a 'roles' array.

**JSON Output Structure:**
{{
  ""projectScale"": ""Medium"",
  ""totalDurationDays"": {idealDuration},
  ""sequencingNotes"": ""The total duration is less than the sum of phases due to a planned overlap between the Development and Testing phases."",
  ""phases"": [
    {{ ""phaseName"": ""Analysis & Design"", ""durationDays"": 15, ""sequenceType"": ""Serial"" }},
    {{ ""phaseName"": ""Development"", ""durationDays"": 40, ""sequenceType"": ""Subsequent"" }}
  ]
}}

Return only the JSON object.";
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
