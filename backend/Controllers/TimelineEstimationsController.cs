using System;
using System.Collections.Generic;
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
    public async Task<ActionResult<TimelineEstimationRecord>> GenerateEstimation(TimelineEstimationRequest request)
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
            return Ok(record);
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
    public async Task<ActionResult<TimelineEstimationRecord>> GetEstimation(int assessmentId)
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

        return Ok(record);
    }
}
