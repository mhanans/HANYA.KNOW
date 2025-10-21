using System.Security.Claims;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectTemplatesController : ControllerBase
{
    private readonly ProjectTemplateStore _templates;

    public ProjectTemplatesController(ProjectTemplateStore templates)
    {
        _templates = templates;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-project-templates", "pre-sales-assessment-workspace")]
    public async Task<ActionResult<List<ProjectTemplateMetadata>>> Get()
    {
        var list = await _templates.ListMetadataAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    [UiAuthorize("pre-sales-project-templates")]
    public async Task<ActionResult<ProjectTemplate>> Get(int id)
    {
        var template = await _templates.GetAsync(id);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost]
    [UiAuthorize("pre-sales-project-templates")]
    public async Task<ActionResult<ProjectTemplate>> Post(ProjectTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.TemplateName))
        {
            return BadRequest("Template name is required");
        }

        template.Id = null;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var id = await _templates.CreateAsync(template, int.TryParse(userId, out var uid) ? uid : null);
        template.Id = id;
        return CreatedAtAction(nameof(Get), new { id }, template);
    }

    [HttpPut("{id}")]
    [UiAuthorize("pre-sales-project-templates")]
    public async Task<IActionResult> Put(int id, ProjectTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.TemplateName))
        {
            return BadRequest("Template name is required");
        }

        template.Id = id;
        try
        {
            await _templates.UpdateAsync(id, template);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [UiAuthorize("pre-sales-project-templates")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _templates.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
