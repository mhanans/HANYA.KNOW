using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PicsController : ControllerBase
{
    private readonly PicStore _store;

    public PicsController(PicStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<List<Pic>>> Get()
    {
        return await _store.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Pic>> Post(PicRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        var id = await _store.CreateAsync(request.Name, request.Availability, request.CategoryIds ?? new List<int>());
        var pic = new Pic { Id = id, Name = request.Name, Availability = request.Availability, CategoryIds = request.CategoryIds ?? new List<int>() };
        return CreatedAtAction(nameof(Get), new { id }, pic);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, PicRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        try
        {
            await _store.UpdateAsync(id, request.Name, request.Availability, request.CategoryIds ?? new List<int>());
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
    }
}

public class PicRequest
{
    public string Name { get; set; } = string.Empty;
    public bool Availability { get; set; }
    public List<int>? CategoryIds { get; set; }
}
