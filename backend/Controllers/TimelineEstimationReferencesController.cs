using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[UiAuthorize("timeline-estimation-references")]
public class TimelineEstimationReferencesController : ControllerBase
{
    private readonly TimelineEstimationReferenceStore _store;

    public TimelineEstimationReferencesController(TimelineEstimationReferenceStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<List<TimelineEstimationReference>>> Get(CancellationToken cancellationToken)
    {
        var list = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        return list;
    }

    [HttpPost]
    public async Task<ActionResult<TimelineEstimationReference>> Post(TimelineEstimationReferenceRequest request, CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (!string.IsNullOrEmpty(validationError))
        {
            return BadRequest(validationError);
        }

        var entity = request.ToEntity();
        var id = await _store.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
        entity.Id = id;
        return CreatedAtAction(nameof(Get), new { id }, entity);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, TimelineEstimationReferenceRequest request, CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (!string.IsNullOrEmpty(validationError))
        {
            return BadRequest(validationError);
        }

        var entity = request.ToEntity();
        entity.Id = id;
        try
        {
            await _store.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static string? ValidateRequest(TimelineEstimationReferenceRequest request)
    {
        if (request == null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.ProjectScale))
        {
            return "ProjectScale is required.";
        }

        if (request.TotalDurationDays <= 0)
        {
            return "TotalDurationDays must be greater than zero.";
        }

        if (request.PhaseDurations == null || request.PhaseDurations.Count == 0)
        {
            return "At least one phase duration is required.";
        }

        foreach (var (phase, duration) in request.PhaseDurations)
        {
            if (string.IsNullOrWhiteSpace(phase))
            {
                return "PhaseDurations cannot contain empty phase names.";
            }

            if (duration <= 0)
            {
                return $"Phase '{phase}' must have a duration greater than zero.";
            }
        }

        if (request.ResourceAllocation == null || request.ResourceAllocation.Count == 0)
        {
            return "ResourceAllocation must contain at least one role.";
        }

        foreach (var (role, headcount) in request.ResourceAllocation)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "ResourceAllocation cannot contain empty role names.";
            }

            if (headcount <= 0)
            {
                return $"Role '{role}' must have a headcount greater than zero.";
            }
        }

        return null;
    }
}

public class TimelineEstimationReferenceRequest
{
    public string ProjectScale { get; set; } = string.Empty;
    public Dictionary<string, int>? PhaseDurations { get; set; }
    public int TotalDurationDays { get; set; }
    public Dictionary<string, double>? ResourceAllocation { get; set; }

    public TimelineEstimationReference ToEntity()
    {
        var phases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (PhaseDurations != null)
        {
            foreach (var (key, value) in PhaseDurations)
            {
                if (!string.IsNullOrWhiteSpace(key) && value > 0)
                {
                    phases[key.Trim()] = value;
                }
            }
        }

        var resources = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (ResourceAllocation != null)
        {
            foreach (var (key, value) in ResourceAllocation)
            {
                if (!string.IsNullOrWhiteSpace(key) && value > 0)
                {
                    resources[key.Trim()] = value;
                }
            }
        }

        return new TimelineEstimationReference
        {
            ProjectScale = ProjectScale.Trim(),
            PhaseDurations = phases,
            TotalDurationDays = TotalDurationDays,
            ResourceAllocation = resources
        };
    }
}
