using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[UiAuthorize("documents")]
public class DocumentsController : ControllerBase
{
    private readonly VectorStore _store;

    public DocumentsController(VectorStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentInfo>>> Get()
        => Ok(await _store.ListDocumentsAsync());

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] DocumentUpdate request)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
            return BadRequest("Source is required.");
        await _store.UpdateDocumentCategoryAsync(request.Source, request.CategoryId);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return BadRequest("Source is required.");
        await _store.DeleteDocumentAsync(source);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DocumentSummary>> Summary([FromQuery] string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return BadRequest("Source is required.");
        var summary = await _store.GetDocumentSummaryAsync(source) ?? string.Empty;
        return Ok(new DocumentSummary(source, summary));
    }
}

public record DocumentUpdate(string Source, int? CategoryId);

public record DocumentSummary(string Source, string Summary);

