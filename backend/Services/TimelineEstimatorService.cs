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
        var references = await _referenceStore.ListAsync(cancellationToken).ConfigureAwait(false);

        var prompt = BuildEstimatorPrompt(assessment, estimationColumnEffort, activityManDays, roleManDays, references, config);
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
        Dictionary<string, double> estimationColumnManDays,
        Dictionary<string, double> activityManDays,
        Dictionary<string, double> roleManDays,
        IReadOnlyList<TimelineEstimationReference> references,
        PresalesConfiguration config)
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

        var columnLines = estimationColumnManDays
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
                $"  - Estimation Column: \"{kvp.Key}\" => {kvp.Value.ToString("F2", CultureInfo.InvariantCulture)} man-days");
        var activityLines = activityManDays
            .OrderBy(kvp => activityOrderLookup.TryGetValue(kvp.Key, out var order) ? order : int.MaxValue)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
                $"  - Phase: \"{kvp.Key}\" => {kvp.Value.ToString("F2", CultureInfo.InvariantCulture)} man-days");
        var roleLines = roleManDays
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
                $"  - Role: \"{kvp.Key}\" => {kvp.Value.ToString("F2", CultureInfo.InvariantCulture)} man-days");
        var referenceLines = (references ?? Array.Empty<TimelineEstimationReference>())
            .OrderBy(r => r.ProjectScale, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var phaseSummary = string.Join(", ", r.PhaseDurations
                    .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(k => $"{k.Key}: {k.Value}d"));
                var resourceSummary = string.Join(", ", r.ResourceAllocation
                    .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(k => $"{k.Key}: {k.Value.ToString("F1", CultureInfo.InvariantCulture)}"));
                return $"  - Scale {r.ProjectScale}: {phaseSummary} | Total: {r.TotalDurationDays}d | Resources: {resourceSummary}";
            });

        return $@"
        You are an expert project planning AI. Based on the assessment data provided, generate an estimated timeline summary that will guide a downstream scheduling agent.

        **Inputs:**
        - Project: {assessment.ProjectName}
        - Template: {assessment.TemplateName}
        - Estimation Column Effort:
{string.Join("\n", columnLines)}
        - Phase Effort Summary:
{string.Join("\n", activityLines)}
        - Role Effort Summary:
{string.Join("\n", roleLines)}
        - Historical Reference Table:
{string.Join("\n", referenceLines)}

        **Requirements:**
        1. Determine the project scale (Long, Medium, Short) using the total man-days and the historical reference table.
        2. Based on the total effort across all roles, estimate a realistic total project duration in days. Assume a core project team is typically small (e.g., 2-4 full-time equivalents). Find a balance between duration and team size when deciding the duration.
        3. Provide a duration (in days) for each phase. Decide if a phase should be Serial, Subsequent, or Parallel.
        4. Your total project duration may differ from the sum of phase durations due to overlaps. Explain this in the sequencing notes.
        5. Output strictly valid minified JSON with the following structure. DO NOT include a 'roles' array in the JSON.
           {{
             ""projectScale"": ""Medium"",
             ""totalDurationDays"": 45,
             ""sequencingNotes"": ""Total shorter than sum because development and testing overlap."",
             ""phases"": [
               {{ ""phaseName"": ""Discovery"", ""durationDays"": 5, ""sequenceType"": ""Serial"" }}
             ]
           }}
        ""sequenceType"" MUST be one of ""Serial"", ""Subsequent"", or ""Parallel"".
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
