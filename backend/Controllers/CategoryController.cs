using backend.Services;
using Microsoft.AspNetCore.Mvc;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[UiAuthorize("categories")]
public class CategoriesController : ControllerBase
{
    private readonly CategoryStore _store;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(CategoryStore store, ILogger<CategoriesController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Category>>> Get()
    {
        return await _store.ListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Category>> Post(CategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        var id = await _store.CreateAsync(request.Name);
        var cat = new Category { Id = id, Name = request.Name };
        return CreatedAtAction(nameof(Get), new { id }, cat);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, CategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        try
        {
            await _store.UpdateAsync(id, request.Name);
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

public class CategoryRequest
{
    public string Name { get; set; } = string.Empty;
}
