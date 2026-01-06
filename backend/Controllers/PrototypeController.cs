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
using Microsoft.Extensions.Logging;

namespace backend.Controllers;

[ApiController]
[Route("api/prototypes")]
public class PrototypeController : ControllerBase
{
    private readonly ProjectAssessmentStore _assessments;
    private readonly PrototypeGenerationService _prototypeService;
    private readonly PrototypeStore _prototypeStore;
    private readonly ILogger<PrototypeController> _logger;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public PrototypeController(
        ProjectAssessmentStore assessments,
        PrototypeGenerationService prototypeService,
        PrototypeStore prototypeStore,
        ILogger<PrototypeController> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _assessments = assessments;
        _prototypeService = prototypeService;
        _prototypeStore = prototypeStore;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-prototypes")]
    public async Task<ActionResult<IEnumerable<PrototypeAssessmentSummary>>> ListAssessments()
    {
        // [VALIDATION-MARKER] Filter by User Account
        // Uncomment the lines below to enable strict user-based filtering. 
        // Currently disabled/configurable to allow seeing all assessments.
        // var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // var userId = int.TryParse(userIdClaim, out var parsed) ? parsed : (int?)null;
        int? userId = null; // Default to null (All Users) when validation is disabled

        var histories = await _assessments.ListAsync(userId).ConfigureAwait(false);
        var completed = histories
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingPrototypes = await _prototypeStore.ListAsync();
        
        var results = completed
            .Select(a => {
                var hasProto = existingPrototypes.TryGetValue(a.Id, out var record);
                
                return new PrototypeAssessmentSummary
                {
                    AssessmentId = a.Id,
                    ProjectName = a.ProjectName,
                    TemplateName = a.TemplateName,
                    Status = a.Status,
                    LastModifiedAt = a.LastModifiedAt,
                    HasPrototype = hasProto,
                    PrototypeStatus = hasProto ? record.Status : "None"
                };
            })
            .OrderByDescending(r => r.LastModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.AssessmentId)
            .ToList();

        return Ok(results);
    }

    [HttpPost("generate")]
    [UiAuthorize("pre-sales-prototypes")]
    public async Task<ActionResult<object>> Generate([FromBody] GeneratePrototypeRequest request)
    {
        if (request == null || request.AssessmentId <= 0)
        {
            return BadRequest("AssessmentId is required.");
        }

        try
        {
            await _prototypeService.StartGenerationAsync(request.AssessmentId, request.ItemIds, request.ItemFeedback);
            return Accepted(new { message = "Prototype generation started in background." });
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
             return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("{assessmentId}/download")]
    [UiAuthorize("pre-sales-prototypes")]
    public async Task<IActionResult> Download(int assessmentId)
    {
         var assessment = await _assessments.GetAsync(assessmentId);
         if (assessment == null) return NotFound("Assessment not found");

        var prototypePath = _configuration["PrototypeStoragePath"];
        if (string.IsNullOrWhiteSpace(prototypePath))
        {
             prototypePath = Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? AppContext.BaseDirectory, "frontend", "public", "demos");
             if (!Directory.Exists(prototypePath)) prototypePath = Path.Combine(AppContext.BaseDirectory, "demos");
        }
        
        var outputDir = Path.Combine(prototypePath, assessmentId.ToString());
        
        if (!Directory.Exists(outputDir))
        {
             _logger.LogWarning("Prototype not found at: {Path}", outputDir);
             return NotFound("Prototype files not found on server.");
        }

        try
        {
             var zipBytes = await _prototypeService.GetZipBytesAsync(assessmentId, outputDir);
             var safeProjectName = string.IsNullOrWhiteSpace(assessment?.ProjectName)
                ? assessmentId.ToString()
                : assessment?.ProjectName.Replace(" ", "_");
             return File(zipBytes, "application/zip", $"Prototype-{safeProjectName}.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error downloading prototype for {Id}", assessmentId);
            return StatusCode(500, ex.Message);
        }
    }
}

public class PrototypeAssessmentSummary
{
    public int AssessmentId { get; set; }
    public string? ProjectName { get; set; }
    public string? TemplateName { get; set; }
    public string? Status { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public bool HasPrototype { get; set; }
    public string? PrototypeStatus { get; set; }
}

public class GeneratePrototypeRequest
{
    public int AssessmentId { get; set; }
    public List<string>? ItemIds { get; set; } // Optional: For re-generating specific items
    public Dictionary<string, string>? ItemFeedback { get; set; } // Optional: Feedback per item
}
