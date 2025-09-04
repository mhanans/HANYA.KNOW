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
        if (string.IsNullOrWhiteSpace(request.TicketNumber) || string.IsNullOrWhiteSpace(request.Complaint) || string.IsNullOrWhiteSpace(request.Detail))
            return BadRequest("All fields are required.");
        var id = await _store.CreateAsync(request.TicketNumber, request.Complaint, request.Detail);
        var ticket = new Ticket
        {
            Id = id,
            TicketNumber = request.TicketNumber,
            Complaint = request.Complaint,
            Detail = request.Detail,
            CreatedAt = DateTime.UtcNow
        };
        var (categoryId, picId) = await _assigner.AutoAssignAsync(ticket);
        ticket.CategoryId = categoryId;
        ticket.PicId = picId;
        return CreatedAtAction(nameof(Get), new { id }, ticket);
    }

    [HttpPut("{id}/assign")]
    public async Task<IActionResult> Assign(int id, TicketAssignRequest request)
    {
        try
        {
            await _store.AssignAsync(id, request.CategoryId, request.PicId);
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
    public string TicketNumber { get; set; } = string.Empty;
    public string Complaint { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class TicketAssignRequest
{
    public int CategoryId { get; set; }
    public int PicId { get; set; }
}
