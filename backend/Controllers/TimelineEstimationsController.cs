using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/timeline-estimations")]
public class TimelineEstimationsController : ControllerBase
{
    private readonly TimelineEstimationStore _estimationStore;
    private readonly TimelineEstimatorService _estimatorService;
    private readonly TimelineGenerationService _generationService;
    private readonly TimelineStore _timelineStore;

    public TimelineEstimationsController(
        TimelineEstimationStore estimationStore,
        TimelineEstimatorService estimatorService,
        TimelineGenerationService generationService,
        TimelineStore timelineStore)
    {
        _estimationStore = estimationStore;
        _estimatorService = estimatorService;
        _generationService = generationService;
        _timelineStore = timelineStore;
    }

    [HttpPost]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineEstimationDetails>> GenerateEstimation(TimelineEstimationRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        try
        {
            var record = await _estimatorService
                .GenerateAsync(request.AssessmentId, userId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            return Ok(BuildResponse(record));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ... GetEstimation ...

    [HttpGet("recommendation/{assessmentId}")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineEstimatorService.TeamRecommendation>> GetTeamRecommendation(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        try
        {
            var recommendation = await _estimatorService
                .RecommendTeamAsync(assessmentId, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            return Ok(recommendation);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("generate-strict")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineRecord>> GenerateStrictEstimation(GenerateStrictRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        try
        {
            // 1. Generate Strict Estimation (The Plan)
            var strictEst = await _estimatorService
                .GenerateStrictAsync(request.AssessmentId, request.ConfirmedTeam, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            // 2. Generate Deterministic Timeline (The Chart)
            // This bypasses the AI Generation Service to ensure strict adherence to the Template's item structure.
            var timeline = await _estimatorService
                .GenerateTimelineFromStrictAsync(request.AssessmentId, strictEst, request.BufferPercentage, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            
            // 3. Persist
            await _timelineStore.SaveAsync(timeline, HttpContext.RequestAborted).ConfigureAwait(false);

            return Ok(timeline);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public class GenerateStrictRequest
    {
        public int AssessmentId { get; set; }
        public List<TimelineRoleEstimate> ConfirmedTeam { get; set; } = new();
        public int BufferPercentage { get; set; } = 20;
    }

    private static TimelineEstimationDetails BuildResponse(TimelineEstimationRecord record)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        var estimationResult = new TimelineEstimationRecord
        {
            AssessmentId = record.AssessmentId,
            ProjectName = record.ProjectName,
            TemplateName = record.TemplateName,
            GeneratedAt = record.GeneratedAt,
            ProjectScale = record.ProjectScale,
            TotalDurationDays = record.TotalDurationDays,
            SequencingNotes = record.SequencingNotes,
            Phases = (record.Phases ?? new List<TimelinePhaseEstimate>())
                .Select(phase => new TimelinePhaseEstimate
                {
                    PhaseName = phase?.PhaseName ?? string.Empty,
                    DurationDays = phase?.DurationDays ?? 0,
                    SequenceType = phase?.SequenceType ?? string.Empty
                })
                .ToList(),
            Roles = (record.Roles ?? new List<TimelineRoleEstimate>())
                .Select(role => new TimelineRoleEstimate
                {
                    Role = role?.Role ?? string.Empty,
                    EstimatedHeadcount = role?.EstimatedHeadcount ?? 0,
                    TotalManDays = role?.TotalManDays ?? 0
                })
                .ToList(),
            RawInputData = null
        };

        return new TimelineEstimationDetails
        {
            EstimationResult = estimationResult,
            RawInput = record.RawInputData
        };
    }
}
