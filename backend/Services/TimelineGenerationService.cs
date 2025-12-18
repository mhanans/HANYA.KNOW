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
        // V1 uses V0 as a reference for sequencing but enforces strict durations.
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
        if (estimation == null) throw new ArgumentNullException(nameof(estimation));
        if (estimation.TotalDurationDays <= 0) throw new ArgumentOutOfRangeException(nameof(estimation.TotalDurationDays));
        if (ganttTasks == null || ganttTasks.Count == 0) throw new InvalidOperationException("Granular tasks are required.");

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
                         var constraintStart = Math.Max(1, item.StartDayOffset);
                         var constraintDuration = Math.Max(1, item.Duration);
                         
                         var matchingTask = ganttTasks.FirstOrDefault(t => 
                             t.Detail.Contains(item.Name, StringComparison.OrdinalIgnoreCase) ||
                             item.Name.Contains(t.Detail, StringComparison.OrdinalIgnoreCase));
                         
                         var taskNameForConstraint = matchingTask != null ? matchingTask.Detail : item.Name;
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

        return $@"
    You are a hyper-logical, deterministic Project Scheduling engine. Your only function is to convert a list of tasks into a valid, compact, day-by-day Gantt chart in JSON format. You must follow all rules PERFECTLY. Your output must be a single, clean JSON object.

    **1. MANDATORY CONSTRAINTS (NON-NEGOTIABLE):**
    *   **Total Project Duration:** The schedule must fit exactly within **{estimation.TotalDurationDays} days**.
    *   **Resource Availability (Headcount):** The total man-days for any role on any single day CANNOT exceed the available headcount.
        {resourceConstraints}
    *   **Discrete Manpower Rule:** Daily effort must be **EXACTLY 0.5 or 1.0**.
    *   **Compact Scheduling:** Start every task AS EARLY AS POSSIBLE (ASAP).

    **2. HIGH-LEVEL PHASE PLAN:**
    {string.Join("\n", phaseGuidanceLines)}

    **3. TEMPLATE-ENFORCED CONSTRAINTS:**
    {string.Join("\n", constraintLines)}

    **4. DETAILED TASK LIST TO SCHEDULE:**
    [
    {string.Join(",\n", taskLines)}
    ]

    **5. EXAMPLE OUTPUT:**
    ```json
    {exampleJson}
    ```

    **FINAL JSON OUTPUT (Strictly this format, no commentary):**
    ";
    }

    private string ConstructDetailedAiPrompt(
        TimelineEstimationRecord estimation,
        List<AssessmentTaskAggregator.GanttTask> ganttTasks,
        ProjectTemplate? template,
        TimelineRecord version0)
    {
        static string Encode(string? value) => JsonEncodedText.Encode(value ?? string.Empty).ToString();

        // Summarize Version 0 for context
        var v0Summary = version0.Activities?.SelectMany(a => a.Details)
            .Select(d => $"  - {d.TaskName} (Starts: Day {d.StartDay}, Duration: {d.DurationDays})")
            .ToList() ?? new List<string>();

        // 1. EXTRACT HEADCOUNTS FROM UI INPUT
        var roles = estimation.Roles ?? new List<TimelineRoleEstimate>();

        double GetHeadcount(string targetRole)
        {
            var match = roles.FirstOrDefault(r => 
                string.Equals(r.Role, targetRole, StringComparison.OrdinalIgnoreCase) || 
                r.Role.Contains(targetRole, StringComparison.OrdinalIgnoreCase));
            
            if (match != null && match.EstimatedHeadcount > 0) return match.EstimatedHeadcount;
            // Fallbacks
            if (targetRole.Contains("Project Manager", StringComparison.OrdinalIgnoreCase) || targetRole.Contains("PM", StringComparison.OrdinalIgnoreCase)) return 0.5;
            if (targetRole.Contains("Architect", StringComparison.OrdinalIgnoreCase)) return 0.5;
            return 1.0; 
        }

        double devCount = GetHeadcount("Developer");
        double baCount = GetHeadcount("Business Analyst");
        double archCount = GetHeadcount("Architect");
        double pmCount = GetHeadcount("Project Manager");

        // 2. PRE-CALCULATE DURATION CONSTRAINTS
        var taskLines = ganttTasks.Select(t =>
        {
            double headcount = 1.0;
            if (t.Actor.Contains("Developer", StringComparison.OrdinalIgnoreCase)) headcount = devCount;
            else if (t.Actor.Contains("Business Analyst", StringComparison.OrdinalIgnoreCase)) headcount = baCount;
            else if (t.Actor.Contains("Architect", StringComparison.OrdinalIgnoreCase)) headcount = archCount;
            else if (t.Actor.Contains("Project Manager", StringComparison.OrdinalIgnoreCase) || t.Actor.Contains("PM", StringComparison.OrdinalIgnoreCase)) headcount = pmCount;

            if (headcount <= 0.01) headcount = 1.0;

            double rawDuration = t.ManDays / headcount;
            double bufferedDuration = rawDuration * 1.2;
            int finalDuration = (int)Math.Ceiling(bufferedDuration);
            finalDuration = Math.Max(1, finalDuration);

            return $"  - {{ \"taskName\": \"{Encode(t.Detail)}\", \"actor\": \"{Encode(t.Actor)}\", \"manDays\": {t.ManDays.ToString("F2", CultureInfo.InvariantCulture)}, \"ASSIGNED_HEADCOUNT\": {headcount.ToString("F1", CultureInfo.InvariantCulture)}, \"REQUIRED_DURATION\": {finalDuration} }}";
        });

        var exampleJson = """
    {
      "totalDurationDays": 45,
      "activities": [
        { 
          "activityName": "Project Preparation", 
          "details": [ 
            { "taskName": "System Setup", "actor": "Architect", "manDays": 1.2, "startDay": 1, "durationDays": 3 } 
          ] 
        }
      ],
      "resourceAllocation": [ 
        { "role": "Architect", "totalManDays": 1.5, "dailyEffort": [0.5, 0.5, 0.5] } 
      ]
    }
    """;

        return $@"
# Role
You are a Strict Logic Project Scheduler. Your goal is to REFINE a draft schedule (V0) into a perfect Final Schedule (V1).

# INPUTS
1. **Draft Schedule (V0)** - Use this for SEQUENCING (Order of tasks):
{string.Join("\n", v0Summary.Take(50)) /* Limit to 50 for context window safety if needed, or send all */}
... (and so on)

2. **Strict Constraints (Non-Negotiable)**:
- **Developer**: {devCount:F1} Person(s).
- **Business Analyst**: {baCount:F1} Person(s).
- **Architect**: {archCount:F1} Person(s).
- **Project Manager**: {pmCount:F1} Person(s).

# YOUR TASK
Copy the **SEQUENCING** (ordering) from the Draft Schedule (V0), BUT:
1. **FIX THE DURATIONS**: You MUST use the `REQUIRED_DURATION` calculated below. Do NOT use the V0 durations.
2. **FIX THE GAPS**: Remove any unnecessary gaps between tasks. Use the strict calculated duration.
3. **ENFORCE RESOURCE RULES**: Ensure minimal effort (0.5/day) for PM/Architect and full continuity for others.

# RULE 1: DURATION AUTHORITY (CRITICAL)
The `REQUIRED_DURATION` in the Task List is the **ONLY** valid duration.
- Formula Used: `(ManDays / Headcount) * 1.2`.
- You cannot change this. If V0 says '5 days' but `REQUIRED_DURATION` says '3', USE 3.

# RULE 2: SEQUENCING AUTHORITY
- Use the Draft Schedule (V0) to determine which task comes after which.
- If V0 has 'Task A' then 'Task B', you keep that order.
- **Exception**: Parallel Phases.
  - 'Project Preparation' and 'Requirements' should start on **Day 1** (Parallel).
  - 'Sprint Planning' starts after 'Requirements'.

# TASK LIST (With Calculated Durations)
[
{string.Join(",\n", taskLines)}
]

# OUTPUT
Generate the JSON schedule.
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
