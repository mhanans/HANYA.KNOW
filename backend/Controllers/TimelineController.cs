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
[Route("api/timelines")]
public class TimelineController : ControllerBase
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly TimelineStore _timelineStore;
    private readonly TimelineGenerationService _generationService;

    public TimelineController(
        ProjectAssessmentStore assessments,
        TimelineStore timelineStore,
        TimelineGenerationService generationService)
    {
        _assessments = assessments;
        _timelineStore = timelineStore;
        _generationService = generationService;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<IEnumerable<TimelineAssessmentSummary>>> ListAssessments()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        var histories = await _assessments.ListAsync(userId).ConfigureAwait(false);
        var completed = histories
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var summaries = await _timelineStore.ListSummariesAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var results = completed
            .Select(a =>
            {
                summaries.TryGetValue(a.Id, out var existing);
                return new TimelineAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasTimeline = existing?.HasTimeline ?? false,
                    TimelineGeneratedAt = existing?.TimelineGeneratedAt
                };
            })
            .OrderByDescending(r => r.LastModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.AssessmentId)
            .ToList();

        return Ok(results);
    }

    [HttpPost]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<ActionResult<TimelineRecord>> GenerateTimeline(TimelineGenerationRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        try
        {
            var record = await _generationService.GenerateAsync(request.AssessmentId, userId, HttpContext.RequestAborted).ConfigureAwait(false);
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
    public async Task<ActionResult<TimelineRecord>> GetTimeline(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var record = await _timelineStore.GetAsync(assessmentId, HttpContext.RequestAborted).ConfigureAwait(false);
        if (record == null)
        {
            return NotFound();
        }

        return Ok(record);
    }
}
