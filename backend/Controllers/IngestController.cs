using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig;
using Microsoft.Extensions.Logging;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestController : ControllerBase
{
    private readonly VectorStore _store;
    private readonly ILogger<IngestController> _logger;

    public IngestController(VectorStore store, ILogger<IngestController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Post([FromForm] IngestForm form)
    {
        _logger.LogInformation("Ingest request received: {FileCount} files, text length {TextLength}, category {Category}",
            form.Files?.Count ?? 0, form.Text?.Length ?? 0, form.CategoryId);

        if ((form.Files == null || form.Files.Count == 0) && string.IsNullOrWhiteSpace(form.Text))
            return BadRequest("Provide one or more PDF files in 'files' or text in the 'text' field.");

        if (form.Files != null)
        {
            foreach (var file in form.Files)
            {
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Unsupported file type. Only PDF files are accepted.");
                try
                {
                    await using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    ms.Position = 0;
                    using var pdf = PdfDocument.Open(ms);
                    var pageNo = 1;
                    foreach (var page in pdf.GetPages())
                    {
                        var text = page.Text;
                        if (string.IsNullOrWhiteSpace(text)) { pageNo++; continue; }
                        try
                        {
                            await _store.IngestAsync(file.FileName, pageNo, text, form.CategoryId);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogError(ex, "Failed to store document {File} page {Page}", file.FileName, pageNo);
                            return Problem(detail: ex.Message, statusCode: 502, title: $"Failed to store document '{file.FileName}'");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected failure storing document {File} page {Page}", file.FileName, pageNo);
                            return Problem(detail: ex.Message, statusCode: 500, title: $"Failed to store document '{file.FileName}'");
                        }
                        pageNo++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read PDF {File}", file.FileName);
                    return BadRequest($"Failed to read PDF '{file.FileName}': {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(form.Text))
        {
            var title = string.IsNullOrWhiteSpace(form.Title)
                ? (form.Text.Length > 30 ? form.Text[..30] + "..." : form.Text)
                : form.Title;
            int chunkIdx = 1;
            foreach (var chunk in Chunk(form.Text, 500))
            {
                try
                {
                    await _store.IngestAsync(title, null, chunk, form.CategoryId);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Failed to store text chunk {Index} for {Title}", chunkIdx, title);
                    return Problem(detail: ex.Message, statusCode: 502, title: $"Failed to store document '{title}'");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected failure storing text chunk {Index} for {Title}", chunkIdx, title);
                    return Problem(detail: ex.Message, statusCode: 500, title: $"Failed to store document '{title}'");
                }
                chunkIdx++;
            }
        }

        return Ok();
    }
}

public class IngestForm
{
    public List<IFormFile>? Files { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public int? CategoryId { get; set; }
}

static IEnumerable<string> Chunk(string text, int size)
{
    for (int i = 0; i < text.Length; i += size)
        yield return text.Substring(i, Math.Min(size, text.Length - i));
}

static IEnumerable<string> Chunk(string text, int size)
{
    for (int i = 0; i < text.Length; i += size)
        yield return text.Substring(i, Math.Min(size, text.Length - i));
}
