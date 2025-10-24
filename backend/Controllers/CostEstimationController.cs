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
[Route("api/cost-estimations")]
public class CostEstimationController : ControllerBase
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly TimelineStore _timelineStore;
    private readonly CostEstimationService _service;
    private readonly CostEstimationConfigurationStore _configurationStore;

    public CostEstimationController(
        ProjectAssessmentStore assessments,
        TimelineStore timelineStore,
        CostEstimationService service,
        CostEstimationConfigurationStore configurationStore)
    {
        _assessments = assessments;
        _timelineStore = timelineStore;
        _service = service;
        _configurationStore = configurationStore;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<IEnumerable<TimelineAssessmentSummary>>> ListAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        var assessments = await _assessments.ListAsync(userId).ConfigureAwait(false);
        var summaries = await _timelineStore.ListSummariesAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var results = assessments
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .Select(a =>
            {
                summaries.TryGetValue(a.Id, out var timelineSummary);
                return new TimelineAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasTimeline = timelineSummary?.HasTimeline ?? false,
                    TimelineGeneratedAt = timelineSummary?.TimelineGeneratedAt
                };
            })
            .Where(r => r.HasTimeline)
            .OrderByDescending(r => r.LastModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.AssessmentId)
            .ToList();

        return Ok(results);
    }

    [HttpGet("{assessmentId}")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<CostEstimationResult>> GetAsync(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required");
        }

        var result = await _service.GetAsync(assessmentId, null, HttpContext.RequestAborted).ConfigureAwait(false);
        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost("{assessmentId}")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<CostEstimationResult>> RecalculateAsync(int assessmentId, CostEstimationRequest request)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required");
        }

        var result = await _service.GetAsync(assessmentId, request?.Inputs, HttpContext.RequestAborted).ConfigureAwait(false);
        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost("{assessmentId}/goal-seek")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<GoalSeekResponse>> GoalSeekAsync(int assessmentId, GoalSeekRequest request)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.AdjustableField) || string.IsNullOrWhiteSpace(request.TargetField))
        {
            return BadRequest("A target field and adjustable field are required.");
        }

        var response = await _service.GoalSeekAsync(assessmentId, request, HttpContext.RequestAborted).ConfigureAwait(false);
        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpGet("{assessmentId}/export")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<IActionResult> ExportAsync(int assessmentId)
    {
        return await ExportInternalAsync(assessmentId, null);
    }

    [HttpPost("{assessmentId}/export")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<IActionResult> ExportWithInputsAsync(int assessmentId, CostEstimationRequest request)
    {
        return await ExportInternalAsync(assessmentId, request?.Inputs);
    }

    private async Task<IActionResult> ExportInternalAsync(int assessmentId, CostEstimationInputs? inputs)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required");
        }

        try
        {
            var bytes = await _service.ExportAsync(assessmentId, inputs, HttpContext.RequestAborted).ConfigureAwait(false);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Cost-Estimation-{assessmentId}.xlsx");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("configuration")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<CostEstimationConfiguration>> GetConfigurationAsync()
    {
        var config = await _configurationStore.GetAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(config);
    }

    [HttpPut("configuration")]
    [UiAuthorize("pre-sales-cost-estimations")]
    public async Task<ActionResult<CostEstimationConfiguration>> UpdateConfigurationAsync(CostEstimationConfiguration configuration)
    {
        var saved = await _configurationStore.SaveAsync(configuration, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(saved);
    }
}
