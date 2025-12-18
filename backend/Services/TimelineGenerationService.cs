using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private readonly ProjectTemplateStore _templateStore;
    private readonly TimelineStore _timelineStore;
    private readonly TimelineEstimationStore _estimationStore;
    private readonly LlmClient _llmClient;
    private readonly ILogger<TimelineGenerationService> _logger;

    public TimelineGenerationService(
        ProjectAssessmentStore assessments,
        PresalesConfigurationStore configurationStore,
        ProjectTemplateStore templateStore,
        TimelineStore timelineStore,
        TimelineEstimationStore estimationStore,
        LlmClient llmClient,
        ILogger<TimelineGenerationService> logger)
    {
        _assessments = assessments;
        _configurationStore = configurationStore;
        _templateStore = templateStore;
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

        var template = await _templateStore.GetAsync(assessment.TemplateId).ConfigureAwait(false);
        
        // --- Version 0: Standard Logic ---
        var promptV0 = ConstructDailySchedulerAiPrompt(estimatorRecord, ganttTasks, template);
        _logger.LogInformation("Requesting AI generated timeline (V0) for assessment {AssessmentId}.", assessmentId);

        var recordV0 = await executeAiGeneration(promptV0, 0);

        // --- Version 1: Detailed Logic (User Request) ---
        var promptV1 = ConstructDetailedAiPrompt(estimatorRecord, ganttTasks, template, recordV0);
        _logger.LogInformation("Requesting AI generated timeline (V1) for assessment {AssessmentId}.", assessmentId);
        
        var recordV1 = await executeAiGeneration(promptV1, 1);

        return recordV1;

        async Task<TimelineRecord> executeAiGeneration(string prompt, int version)
        {
            string rawResponse = string.Empty;
            AiTimelineResult aiTimeline;
            try
            {
                rawResponse = await _llmClient.GenerateAsync(prompt).ConfigureAwait(false);
                aiTimeline = ParseAiTimeline(rawResponse);
            }
            catch (Exception ex)
            {
                await LogAttemptSafeAsync(assessmentId, assessment.ProjectName, assessment.TemplateName ?? string.Empty, rawResponse, false, ex.Message, cancellationToken);
                _logger.LogError(ex, "AI generation failed for V{Version}.", version);
                // For V1 failure, we might want to just return V0? But let's fail hard for now as requested.
                throw;
            }

            var record = new TimelineRecord
            {
                AssessmentId = assessmentId,
                Version = version,
                ProjectName = assessment.ProjectName,
                TemplateName = assessment.TemplateName ?? string.Empty,
                GeneratedAt = DateTime.UtcNow,
                TotalDurationDays = aiTimeline.TotalDurationDays,
                Activities = aiTimeline.Activities,
                ResourceAllocation = aiTimeline.ResourceAllocation
            };

            ValidateAiTimeline(record, config);
            // Use the roles with explicit headcounts for allocation logic
            record.ResourceAllocation = CalculateResourceAllocation(record, estimatorRecord.Roles);

            try
            {
                await _timelineStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LogAttemptSafeAsync(assessmentId, assessment.ProjectName, assessment.TemplateName ?? string.Empty, rawResponse, false, $"Failed to persist V{version}: {ex.Message}", cancellationToken);
                throw;
            }

            await LogAttemptSafeAsync(assessmentId, assessment.ProjectName, assessment.TemplateName ?? string.Empty, rawResponse, true, null, cancellationToken);
            return record;
        }
    }

    public async Task<TimelineRecord> GenerateV1AfterStrictAsync(
        int assessmentId, 
        TimelineEstimationRecord strictEstimation, 
        TimelineRecord deterministicV0, 
        CancellationToken cancellationToken)
    {
        var assessment = await _assessments.GetAsync(assessmentId, null).ConfigureAwait(false);
        if (assessment == null) throw new KeyNotFoundException($"Assessment {assessmentId} not found.");
        
        var config = await _configurationStore.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var ganttTasks = AssessmentTaskAggregator.GetGanttTasks(assessment, config);
        var template = await _templateStore.GetAsync(assessment.TemplateId).ConfigureAwait(false);

        var promptV1 = ConstructDetailedAiPrompt(strictEstimation, ganttTasks, template, deterministicV0);
        _logger.LogInformation("Requesting AI generated timeline (V1-Strict) for assessment {AssessmentId}.", assessmentId);

        string rawResponse = string.Empty;
        AiTimelineResult aiTimeline;
        
        try
        {
            rawResponse = await _llmClient.GenerateAsync(promptV1).ConfigureAwait(false);
            aiTimeline = ParseAiTimeline(rawResponse);
        }
        catch (Exception ex)
        {
            await LogAttemptSafeAsync(assessmentId, assessment.ProjectName, assessment.TemplateName ?? string.Empty, rawResponse, false, ex.Message, cancellationToken);
            throw;
        }

        var recordV1 = new TimelineRecord
        {
            AssessmentId = assessmentId,
            Version = 1, // V1
            ProjectName = assessment.ProjectName,
            TemplateName = assessment.TemplateName ?? string.Empty,
            GeneratedAt = DateTime.UtcNow,
            TotalDurationDays = aiTimeline.TotalDurationDays,
            Activities = aiTimeline.Activities,
            ResourceAllocation = aiTimeline.ResourceAllocation
        };

        ValidateAiTimeline(recordV1, config);
        // Use the roles with explicit headcounts for allocation logic
        recordV1.ResourceAllocation = CalculateResourceAllocation(recordV1, strictEstimation.Roles);

        await _timelineStore.SaveAsync(recordV1, cancellationToken).ConfigureAwait(false);
        await LogAttemptSafeAsync(assessmentId, assessment.ProjectName, assessment.TemplateName ?? string.Empty, rawResponse, true, null, cancellationToken);

        return recordV1;
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

    public async Task<List<TimelineResourceAllocationEntry>> RecalculateResourceAllocationAsync(TimelineRecord record, CancellationToken cancellationToken)
    {
        // We need the original estimation to know the "Confirmed Headcounts" and "Roles"
        var estimation = await _estimationStore.GetAsync(record.AssessmentId, cancellationToken).ConfigureAwait(false);
        var roles = estimation?.Roles ?? new List<TimelineRoleEstimate>();
        
        // If no roles found (legacy), try to rebuild reasonable defaults from config or active roles?
        // But the user specifically wants strict behavior "PM follow from start to end".
        // Let's rely on the estimation roles.

        // Fix: Ensure TotalDurationDays covers all activities before calculating allocation
        if (record.Activities != null)
        {
            var maxActivityEnd = record.Activities
                .Where(a => a.Details != null)
                .SelectMany(a => a.Details)
                .Where(d => d.DurationDays > 0)
                .Max(d => (int?)(d.StartDay + d.DurationDays - 1)) ?? 0;
            
            // STRICT RECALCULATION: The duration of the timeline IS the end of the last task.
            // We do not trust the incoming 'TotalDurationDays' from the client as it might be stale.
            record.TotalDurationDays = Math.Max(1, maxActivityEnd);
        }
        
        return CalculateResourceAllocation(record, roles);
    }

    private static List<TimelineResourceAllocationEntry> CalculateResourceAllocation(
        TimelineRecord record,
        List<TimelineRoleEstimate> confirmedRoles)
    {
        var result = new List<TimelineResourceAllocationEntry>();
        if (confirmedRoles == null || !confirmedRoles.Any()) return result;

        var totalDays = record.TotalDurationDays;
        
        // Flatten all tasks to find start/end range per role
        var roleRanges = new Dictionary<string, (int MinStart, int MaxEnd)>(StringComparer.OrdinalIgnoreCase);

        if (record.Activities != null)
        {
            foreach (var act in record.Activities)
            {
                if (act.Details == null) continue;

                foreach (var det in act.Details)
                {
                    if (det.DurationDays <= 0) continue;
                    
                    // Split actor string (e.g., "Business Analyst, Developer")
                    // Adaptation: Match V0 logic
                    var taskActors = (det.Actor ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(s => s.Trim())
                                              .ToList();

                    foreach (var actorCandidate in taskActors)
                    {
                        var rName = actorCandidate;
                        // Adaptation: Match V0 Logic strictly
                        // V0 Logic: match = specific || contains
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
        }

        foreach (var role in confirmedRoles)
        {
            var daily = new double[totalDays + 5]; // +5 buffer
            
            // Logic: PM and Architect -> Always from Day 1 to TotalDays
            // Adaptation: Match V0 Logic explicitly
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

    private string ConstructDailySchedulerAiPrompt(
        TimelineEstimationRecord estimation,
        List<AssessmentTaskAggregator.GanttTask> ganttTasks,
        ProjectTemplate? template)
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

        // 1. Build List of Constraints from Template
        var constraintLines = new List<string>();
        if (template?.TimelinePhases != null)
        {
             foreach(var phase in template.TimelinePhases)
             {
                 if (phase.Items != null)
                 {
                     foreach(var item in phase.Items)
                     {
                         // Calculate Absolute Start Day Constraint: Item Offset is now Absolute
                         var constraintStart = Math.Max(1, item.StartDayOffset);
                         var constraintDuration = Math.Max(1, item.Duration);
                         
                         // Fuzzy Matching to find the exact task name used in GanttTasks
                         // This is critical because Template Item Name might be "Sprint Planning"
                         // while Gantt Task is "Sprint Planning - Requirement..."
                         var matchingTask = ganttTasks.FirstOrDefault(t => 
                             t.Detail.Contains(item.Name, StringComparison.OrdinalIgnoreCase) ||
                             item.Name.Contains(t.Detail, StringComparison.OrdinalIgnoreCase));
                         
                         var taskNameForConstraint = matchingTask != null ? matchingTask.Detail : item.Name;
                         
                         // Constraint is applied with the EXACT name usage
                         constraintLines.Add($"  - \"{Encode(taskNameForConstraint)}\": Must Start on Day {constraintStart} and last {constraintDuration} days.");
                     }
                 }
             }
        }
        
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

        var resourceConstraints = string.Join("\n", (estimation.Roles ?? new List<TimelineRoleEstimate>())
            .Select(r => $"    - {r.Role}: {r.EstimatedHeadcount} person(s) (Max {r.EstimatedHeadcount:F1} man-days per day)"));

        var dependencies = new List<string>();
        var activePredecessors = new List<string>();
        var phases = estimation.Phases ?? new List<TimelinePhaseEstimate>();

        for (int i = 0; i < phases.Count; i++)
        {
            var p = phases[i];
            var phaseName = p.PhaseName;
            
            if (i == 0)
            {
                activePredecessors.Add(phaseName);
                continue;
            }

            // If Serial, it depends on the completion of the previous block(s)
            if (string.Equals(p.SequenceType, "Serial", StringComparison.OrdinalIgnoreCase))
            {
                if (activePredecessors.Count > 0)
                {
                    var preds = string.Join("' AND '", activePredecessors); // e.g. 'Phase A' AND 'Phase B'
                    dependencies.Add($"- All tasks in '{phaseName}' CANNOT start until ALL tasks in '{preds}' are fully completed.");
                }
                
                // For the next block, this Serial phase becomes the new single predecessor
                activePredecessors.Clear();
                activePredecessors.Add(phaseName);
            }
            else
            {
                // If Parallel, it shares the same start time/predecessors as the previous phase (effectively).
                // It does NOT wait for the immediately preceding phase in this list (which acts as its sibling).
                // But generally, the 'activePredecessors' list currently contains the "Previous Phase" (added at end of loop).
                // Wait, logic correction:
                
                // Standard Model:
                // A (Serial) -> Active: [A]
                // B (Serial) -> Depends on [A]. Active becomes [B].
                // C (Parallel to B) -> Depends on [A] (same as B). Active becomes [B, C].
                // D (Serial) -> Depends on [B, C]. Active becomes [D].
                
                // My logic above:
                // i=0: A. Active=[A].
                // i=1: B (Serial). Depends on [A]. Active=[B]. Correct.
                // i=2: C (Parallel). NO dependency generated (so it starts ASAP, conditioned only on previous Serial block... which isn't explicitly linked here, BUT logic implies implicit flow?)
                // Actually, if C generates NO dependency, it might start at Day 0?
                // NO. We need to say "C depends on [A]".
                // BUT [A] is no longer in `activePredecessors`? 
                // Ah, `activePredecessors` tracks the "Tail" of the chain.
                
                // We need to track `previousBlockTail`.
                // Let's refine.
                
                // BETTER LOGIC:
                // Track `lastSerialBlock` (List of phases that formed the last serial barrier).
                // Initially empty? Or implicit start?
                
                // Let's assume strict dependencies. 
                // A phase usually just needs to know what it follows.
                // If Serial: Follows Phase[i-1].
                // If Parallel: Follows Phase[i-1]'s Predecessor.
                
                // Let's rely on the AI interpreting "Sequential" vs "Parallel" if we are explicit about PREDECESSORS.
                // Instead of tracking logic here, let's look at the List.
                
                // Simple Rule:
                // If Phase is Serial, it must start after Phase[i-1] completes.
                // If Phase is Parallel, it must start after Phase[i-1]'s *Start*? No, usually "Parallel" means concurrent with.
                
                // Let's stick to the "Block" logic which is robust for linear flows.
                // We need to store the `predecessorsForCurrentBlock`.
                
                // Re-attempting construction logic:
                // var barrierPhases = new List<string>(); // Phases that MUST finish before the next Serial block
                // var currentBlockPhases = new List<string>(); // Phases in the current parallel group
                
                // Loop:
                // if Serial:
                //    Rule: This Phase depends on `barrierPhases`.
                //    `barrierPhases` = [This Phase]
                // if Parallel:
                //    Rule: This Phase depends on `barrierPhases` (Same as whoever it is parallel with).
                //    `barrierPhases`.Add(This Phase) ?? No.
                //    If B is Serial (after A), barrier is [A]. B depends on A. New Barrier is [B].
                //    If C is Parallel (to B), C depends on [A]. New Barrier is [B, C] (Next guy waits for both).
                
                // This seems correct.
                 
             } 
             
             // Redoing logic in code block below for clarity:
        }
        
        // Revised Implementation to paste:
        var computedDependencies = new List<string>();
        // The set of phases that constitute the "Previous Completed Block". 
        // Any new task must wait for ALL of these to finish.
        var previousBlockPhases = new List<string>(); 
        
        for (int i = 0; i < phases.Count; i++)
        {
            var p = phases[i];
            
            if (i == 0)
            {
                previousBlockPhases.Add(p.PhaseName);
                continue;
            }
            
            if (string.Equals(p.SequenceType, "Serial", StringComparison.OrdinalIgnoreCase))
            {
                // Adds a hard dependency barrier
                if (previousBlockPhases.Count > 0)
                {
                    var predList = string.Join("' AND '", previousBlockPhases);
                    computedDependencies.Add($"- '{p.PhaseName}' must start AFTER '{predList}' are completed.");
                }
                
                // Reset the barrier to just this phase (it becomes the new bottleneck)
                previousBlockPhases.Clear();
                previousBlockPhases.Add(p.PhaseName);
            }
            else // Parallel
            {
                // Parallel means "Run alongside the previous phase(s)". 
                // So it shares the SAME predecessors as the *current* block.
                // It does NOT depend on the *immediately preceding* phase (which is its sibling).
                // However, we must ensure it depends on the *Previous Block* (the ones before the sibling).
                
                // Wait. 'previousBlockPhases' was reset to [Sibling] in the previous step?
                // If B (Serial) ran: reset prev to [B].
                // Now C (Parallel):
                // It should depend on [A]?? 
                // My logic above Reset `previousBlockPhases` too early!
                
                // We need `currentBlockPredecessors`.
                // This is getting complex to track statefully in a single pass without look-behind.
                // BUT, we can just say:
                // "Parallel" means "Join the current tip".
                // "Serial" means "Extend the tip".
                
                // Let's use the explicit `SequenceType` definition:
                // Parallel: Start With Previous.
                // Serial: Start After Previous.
                
                // If C is Parallel to B. B is Serial.
                // B depends on A.
                // C depends on A.
                
                // We need to capture "Who did B depend on?".
                // Let's maintain `lastDependencySet`.
                
                // Simplified approach for the Prompt (Logic v3):
                // Just map phases to their explicit Dependency List.
                // then print lines.
            }
        }
        
        // Actually, let's keep it simple. The AI is smart.
        // We will just explicitly state the `SequenceType` behavior in the dependencies list.
        // "Phase X is SERIAL: It must wait for Phase Y."
        // "Phase Z is PARALLEL: It can start immediately (provided dependencies of Phase Y are met)."
        
        // BUT determining Phase Y's dependencies is the trick.
        
        // Let's go with the "Accumulating Barrier" approach which covers 95% of cases (Standard SDLC).
        // Barrier = [A].
        // Next is Serial B? B depends on Barrier. New Barrier = [B].
        // Next is Parallel C? C depends on Barrier (old one? No, the one B used?).
        // No, if C is parallel to B, C depends on 'What B depended on'.
        // So we shouldn't update Barrier until we finish the group?
        
        // Working Logic:
        var taskDependencies = new StringBuilder();
        var currentBarrier = new List<string>(); // What determines start of current group
        var nextBarrier = new List<string>();    // What current group produces
        
        // Initialize
        if(phases.Count > 0) nextBarrier.Add(phases[0].PhaseName);
        
        for(int i = 1; i < phases.Count; i++) {
            var prev = phases[i-1];
            var curr = phases[i];
            
            if (string.Equals(curr.SequenceType, "Serial", StringComparison.OrdinalIgnoreCase)) {
                 // Serial means we close the previous group.
                 // The 'nextBarrier' (phases gathered so far) becomes `currentBarrier`.
                 currentBarrier = new List<string>(nextBarrier);
                 nextBarrier.Clear();
                 
                 // Curr depends on CurrentBarrier
                 if(currentBarrier.Count > 0) {
                     taskDependencies.AppendLine($"- '{curr.PhaseName}' must wait for completion of: {string.Join(", ", currentBarrier.Select(x => $"'{x}'"))}.");
                 }
                 
                 nextBarrier.Add(curr.PhaseName);
            } else {
                 // Parallel
                 // Curr depends on `currentBarrier` (Same as its siblings).
                 if(currentBarrier.Count > 0) {
                     taskDependencies.AppendLine($"- '{curr.PhaseName}' must wait for completion of: {string.Join(", ", currentBarrier.Select(x => $"'{x}'"))}.");
                 }
                 // And Curr contributes to the Future Barrier
                 nextBarrier.Add(curr.PhaseName);
            }
        }


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

    **3. TASK DEPENDENCIES & CONSTRAINTS:**
    {taskDependencies}
    
    **TEMPLATE-ENFORCED CONSTRAINTS (HIGHEST PRIORITY):**
    These tasks MUST adhere to the specified Start Day and Duration, regardless of other rules, unless it violates valid manpower limits. Use these as anchors.
    {string.Join("\n", constraintLines)}

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

    private string ConstructDetailedAiPrompt(
        TimelineEstimationRecord estimation,
        List<AssessmentTaskAggregator.GanttTask> ganttTasks,
        ProjectTemplate? template,
        TimelineRecord version0)
    {
        static string Encode(string? value) => JsonEncodedText.Encode(value ?? string.Empty).ToString();

        var taskLines = ganttTasks.Select(t =>
            $"  - {{ \"activityGroup\": \"{Encode(t.ActivityGroup)}\", \"taskName\": \"{Encode(t.Detail)}\", \"actor\": \"{Encode(t.Actor)}\", \"manDays\": {t.ManDays.ToString("F2", CultureInfo.InvariantCulture)} }}");

        var phaseLines = (template?.TimelinePhases ?? new List<TimelinePhaseTemplate>())
            .Select(p => $"  - Phase: {p.Name} (Duration: {p.Duration} days, Start: {p.StartDay})");

        var headcountLines = (estimation.Roles ?? new List<TimelineRoleEstimate>())
            .Select(r => $"   - **{r.Role}**: {r.EstimatedHeadcount:F1}");

        // Summarize Version 0 for context
        var v0Summary = version0.Activities?.Select(a => 
            $"- {a.ActivityName} (Starts Day {a.Details.Min(d => d.StartDay)}, Duration {a.Details.Max(d => d.StartDay + d.DurationDays) - a.Details.Min(d => d.StartDay)})")
            ?? Enumerable.Empty<string>();

        var exampleJson = """
    {
      "totalDurationDays": 25,
      "activities": [
        { "activityName": "Project Preparation", "details": [ { "taskName": "System Setup", "actor": "Architect", "manDays": 0.6, "startDay": 1, "durationDays": 1 } ] }
      ],
      "resourceAllocation": [ { "role": "Architect", "totalManDays": 12.0, "dailyEffort": [0.5, 0.5, ...] } ]
    }
    """;

        return $@"
# Role
You are a Senior Technical Project Manager and AI Scheduler. Your goal is to generate a realistic Gantt chart (JSON) based on a Project Assessment and a Timeline Template.

# Inputs
1. **Template**: Timeline Phases and Item definitions.
{string.Join("\n", phaseLines)}

2. **Assessment**: Scoped items with effort estimates (Man-Days).
[
{string.Join(",\n", taskLines)}
]

3. **Headcount Data**:
{string.Join("\n", headcountLines)}

4. **Base Logic Timeline (Reference)**:
The following timeline was generated by a heuristic scheduler. Use it as a baseline but refine it using the rules below.
{string.Join("\n", v0Summary)}

# Logic & Scheduling Rules

## 1. Parallel Phase Start (Efficiency Rule)
Different roles must work in parallel to save time:
- **Project Preparation** (System/DB Setup) is done by **Architect**.
- **Requirements Phase** (SRS/FSD, DB Planning Docs) is done by **Business Analyst**.
- **Rule**: These two phases start on **Day 1** and run in parallel. Do not make Requirements wait for System Setup.

## 2. Strict Dependency Chain
Once the parallel start is complete, follow this sequence:
1. **Sprint Planning**: Starts only after **Requirements** are 100% agreed/done. (Wait for the longer of Prep or Req to finish).
2. **Development**: Starts strictly after Sprint Planning.
   - **Safety Rule**: Developers do not touch code until Sprint Planning is signed off.
   - **Parallelism**: Since Headcount >= 2 (usually), BE and FE tasks run in parallel.
   - **Code Review**: Runs parallel to Dev, finishes slightly later.
3. **Testing Sequence**:
   - **Unit Testing**: During Dev.
   - **SIT**: Starts strictly after **Development & Unit Test** are 100% done.
   - **UAT**: Starts strictly after **SIT** is 100% done.
4. **Closing**: After UAT.

## 3. Duration & Buffer Calculation
- **Base Duration** = (Effort / Headcount).
  - *Exception*: If Headcount < 1, use 1.
- **Buffer**: Apply **20%** to all durations.
- **Rounding**: Round up to the nearest full day.

## 4. Resource Allocation & Daily Effort
- **No Gaps**: Once a resource starts, they work every day until their last task.
- **Project Manager & Architect**:
  - Start Day: Day 1.
  - End Day: Project End.
  - Daily Effort: 0.5 (Constant).
- **Business Analyst**:
  - Start Day: Day 1 (Requirements).
  - End Day: End of Warranty/Support.
  - Daily Effort: Full capacity (e.g. 2.0).
- **Developer**:
  - Start Day: Sprint Planning (First appearance).
  - End Day: End of UAT/Fixes (or Closing if assigned).
  - Daily Effort: Full capacity (e.g. 2.0).

# Task
Generate the `refined_timeline.json`. Ensure the resource allocation arrays match the start/end rules exactly.

**Output strictly valid JSON:**
{exampleJson}
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
