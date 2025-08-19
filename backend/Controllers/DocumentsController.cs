using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
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
}

public record DocumentUpdate(string Source, int? CategoryId);

