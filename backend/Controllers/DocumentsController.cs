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
    private readonly LlmClient _llm;

    public DocumentsController(VectorStore store, LlmClient llm)
    {
        _store = store;
        _llm = llm;
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

    [HttpPost("summary")]
    public async Task<ActionResult<DocumentSummary>> GenerateSummary([FromQuery] string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return BadRequest("Source is required.");
        var preview = await _store.GetDocumentPreviewAsync(source);
        if (string.IsNullOrEmpty(preview))
            return NotFound($"Document '{source}' not found or empty.");
        var prompt = $"Summarize the following document in a concise paragraph:\n{preview}";
        string summary;
        try
        {
            summary = await _llm.GenerateAsync(prompt);
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 502, title: "LLM generation failed");
        }
        await _store.SaveDocumentSummaryAsync(source, summary);
        return Ok(new DocumentSummary(source, summary));
    }
}

public record DocumentUpdate(string Source, int? CategoryId);

public record DocumentSummary(string Source, string Summary);

