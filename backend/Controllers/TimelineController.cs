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
    private readonly TimelineEstimationStore _estimationStore;
    private readonly TimelineGenerationService _generationService;

    public TimelineController(
        ProjectAssessmentStore assessments,
        TimelineStore timelineStore,
        TimelineEstimationStore estimationStore,
        TimelineGenerationService generationService)
    {
        _assessments = assessments;
        _timelineStore = timelineStore;
        _estimationStore = estimationStore;
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
        var estimationSummaries = await _estimationStore.ListSummariesAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var results = completed
            .Select(a =>
            {
                summaries.TryGetValue(a.Id, out var existing);
                estimationSummaries.TryGetValue(a.Id, out var estimation);
                return new TimelineAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasTimeline = existing?.HasTimeline ?? false,
                    TimelineGeneratedAt = existing?.TimelineGeneratedAt,
                    HasTimelineEstimation = estimation != null,
                    TimelineEstimationGeneratedAt = estimation?.GeneratedAt,
                    TimelineEstimationScale = estimation?.ProjectScale
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

    [HttpPut("{assessmentId}")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<IActionResult> UpdateTimeline(int assessmentId, [FromBody] TimelineRecord timeline)
    {
        if (assessmentId <= 0 || timeline == null) return BadRequest("Invalid Data");
        if (assessmentId != timeline.AssessmentId) return BadRequest("Assessment ID mismatch.");
        
        // Optionally Validate?
        // Just save.
        await _timelineStore.SaveAsync(timeline, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok();
    }

    [HttpGet("{assessmentId}/export")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<IActionResult> ExportTimeline(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        var record = await _timelineStore.GetAsync(assessmentId, HttpContext.RequestAborted).ConfigureAwait(false);
        if (record == null)
        {
            return NotFound("Timeline data not found for this assessment.");
        }

        var bytes = TimelineExcelBuilder.Build(record);
        var safeProjectName = string.IsNullOrWhiteSpace(record.ProjectName)
            ? assessmentId.ToString()
            : record.ProjectName.Replace(" ", "_");
        var fileName = $"Timeline-{safeProjectName}.xlsx";

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpDelete("{assessmentId}")]
    [UiAuthorize("pre-sales-project-timelines")]
    public async Task<IActionResult> DeleteTimeline(int assessmentId)
    {
        if (assessmentId <= 0) return BadRequest("Invalid Data");

        await _timelineStore.DeleteAsync(assessmentId, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok();
    }
}
