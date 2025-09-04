using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly TicketStore _store;
    private readonly TicketAssignmentService _assigner;

    public TicketsController(TicketStore store, TicketAssignmentService assigner)
    {
        _store = store;
        _assigner = assigner;
    }

    [HttpGet]
    public async Task<ActionResult<List<Ticket>>> Get()
    {
        return await _store.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Ticket>> Post(TicketRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Complaint) || string.IsNullOrWhiteSpace(request.Detail))
            return BadRequest("All fields are required.");
        var ticket = await _store.CreateAsync(request.Complaint, request.Detail);
        var (categoryId, picId, reason) = await _assigner.AutoAssignAsync(ticket);
        ticket.CategoryId = categoryId;
        ticket.PicId = picId;
        ticket.Reason = reason;
        return CreatedAtAction(nameof(Get), new { ticket.Id }, ticket);
    }

    [HttpPost("{id}/retry-summary")]
    public async Task<ActionResult<Ticket>> RetrySummary(int id)
    {
        try
        {
            var result = await _assigner.RetrySummaryAsync(id);
            if (result == null) return NotFound();
            var ticket = await _store.GetAsync(id);
            if (ticket == null) return NotFound();
            ticket.CategoryId = result.Value.categoryId;
            ticket.PicId = result.Value.picId;
            ticket.Reason = result.Value.reason;
            return ticket;
        }
        catch (Exception ex)
        {
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Summary failed");
        }
    }

    [HttpPut("{id}/assign")]
    public async Task<IActionResult> Assign(int id, TicketAssignRequest request)
    {
        try
        {
            await _store.AssignAsync(id, request.CategoryId, request.PicId, null);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

public class TicketRequest
{
    public string Complaint { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class TicketAssignRequest
{
    public int CategoryId { get; set; }
    public int PicId { get; set; }
}
