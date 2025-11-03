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

        if (string.IsNullOrWhiteSpace(request.PhaseName))
        {
            return "PhaseName is required.";
        }

        if (request.InputManHours <= 0)
        {
            return "InputManHours must be greater than zero.";
        }

        if (request.InputResourceCount <= 0)
        {
            return "InputResourceCount must be greater than zero.";
        }

        if (request.OutputDurationDays <= 0)
        {
            return "OutputDurationDays must be greater than zero.";
        }

        return null;
    }
}

public class TimelineEstimationReferenceRequest
{
    public string PhaseName { get; set; } = string.Empty;
    public int InputManHours { get; set; }
    public int InputResourceCount { get; set; }
    public int OutputDurationDays { get; set; }

    public TimelineEstimationReference ToEntity()
    {
        return new TimelineEstimationReference
        {
            PhaseName = PhaseName.Trim(),
            InputManHours = InputManHours,
            InputResourceCount = InputResourceCount,
            OutputDurationDays = OutputDurationDays
        };
    }
}
