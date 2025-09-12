using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[UiAuthorize("tickets")]
public class TicketCategoriesController : ControllerBase
{
    private readonly TicketCategoryStore _store;
    private readonly ILogger<TicketCategoriesController> _logger;

    public TicketCategoriesController(TicketCategoryStore store, ILogger<TicketCategoriesController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<TicketCategory>>> Get()
    {
        return await _store.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<TicketCategory>> Post(TicketCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TicketType))
            return BadRequest("TicketType is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Description is required.");
        var id = await _store.CreateAsync(request.TicketType, request.Description, request.SampleJson ?? "");
        var cat = new TicketCategory { Id = id, TicketType = request.TicketType, Description = request.Description, SampleJson = request.SampleJson ?? "" };
        return CreatedAtAction(nameof(Get), new { id }, cat);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, TicketCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TicketType) || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("TicketType and Description are required.");
        try
        {
            await _store.UpdateAsync(id, request.TicketType, request.Description, request.SampleJson ?? "");
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _store.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: 409, title: "Delete failed");
        }
    }
}

public class TicketCategoryRequest
{
    public string TicketType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SampleJson { get; set; }
}
