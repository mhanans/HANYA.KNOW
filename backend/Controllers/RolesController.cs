using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController : ControllerBase
{
    private readonly RoleStore _store;

    public RolesController(RoleStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<List<Role>>> Get()
    {
        return await _store.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Role>> Post(RoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        var role = new Role
        {
            Name = request.Name,
            AllCategories = request.AllCategories,
            CategoryIds = request.CategoryIds ?? new List<int>()
        };
        var id = await _store.CreateAsync(role);
        role.Id = id;
        return CreatedAtAction(nameof(Get), new { id }, role);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, RoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        var role = new Role
        {
            Id = id,
            Name = request.Name,
            AllCategories = request.AllCategories,
            CategoryIds = request.CategoryIds ?? new List<int>()
        };
        try
        {
            await _store.UpdateAsync(role);
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

public class RoleRequest
{
    public string Name { get; set; } = string.Empty;
    public bool AllCategories { get; set; }
    public List<int> CategoryIds { get; set; } = new();
}
