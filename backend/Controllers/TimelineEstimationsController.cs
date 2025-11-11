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

    public TimelineEstimationsController(
        TimelineEstimationStore estimationStore,
        TimelineEstimatorService estimatorService)
    {
        _estimationStore = estimationStore;
        _estimatorService = estimatorService;
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

    [HttpGet("{assessmentId}")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineEstimationDetails>> GetEstimation(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var record = await _estimationStore
            .GetAsync(assessmentId, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (record == null)
        {
            return NotFound();
        }

        return Ok(BuildResponse(record));
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
