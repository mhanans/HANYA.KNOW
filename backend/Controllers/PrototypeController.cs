using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/prototypes")]
public class PrototypeController : ControllerBase
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly PrototypeGenerationService _prototypeService;
    private readonly PrototypeStore _prototypeStore;

    public PrototypeController(
        ProjectAssessmentStore assessments,
        PrototypeGenerationService prototypeService,
        PrototypeStore prototypeStore)
    {
        _assessments = assessments;
        _prototypeService = prototypeService;
        _prototypeStore = prototypeStore;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<IEnumerable<PrototypeAssessmentSummary>>> ListAssessments()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;

        var histories = await _assessments.ListAsync(userId).ConfigureAwait(false);
        var completed = histories
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingPrototypes = await _prototypeStore.ListAsync();
        
        var results = completed
            .Select(a => {
                var hasProto = existingPrototypes.ContainsKey(a.Id);
                // Fallback removed to ensure DB consistency. 
                // If a user deletes the DB record, they want to reset the state.
                
                return new PrototypeAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasPrototype = hasProto
                };
            })
            .OrderByDescending(r => r.LastModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.AssessmentId)
            .ToList();

        return Ok(results);
    }

    [HttpPost("generate")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<ActionResult<object>> Generate([FromBody] GeneratePrototypeRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        try
        {
            var url = await _prototypeService.GenerateDemoAsync(request.AssessmentId, request.ItemIds, request.ItemFeedback);
            return Ok(new { url });
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    // ... (rest of File) ...



    [HttpGet("{assessmentId}/download")]
    [UiAuthorize("pre-sales-assessment-workspace")]
    public async Task<IActionResult> Download(int assessmentId)
    {
        if (assessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        try
        {
            var bytes = await _prototypeService.GetZipBytesAsync(assessmentId);
            var assessment = await _assessments.GetAsync(assessmentId);
            var safeProjectName = string.IsNullOrWhiteSpace(assessment?.ProjectName)
                ? assessmentId.ToString()
                : assessment?.ProjectName.Replace(" ", "_");
            
            var fileName = $"Prototype-{safeProjectName}.zip";

            return File(bytes, "application/zip", fileName);
        }
        catch (ArgumentException)
        {
            return NotFound("Assessment not found");
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound("Prototype not generated yet");
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}

public class PrototypeAssessmentSummary
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; }
    public string TemplateName { get; set; }
    public string Status { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public bool HasPrototype { get; set; }
}

public class GeneratePrototypeRequest
{
    public int AssessmentId { get; set; }
    public List<string>? ItemIds { get; set; } // Optional: For re-generating specific items
    public Dictionary<string, string>? ItemFeedback { get; set; } // Optional: Feedback per item
}
