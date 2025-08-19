using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Collections.Generic;
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
        _logger.LogInformation("Ingest request received: {FileCount} files, text length {TextLength}", form.Files?.Count ?? 0, form.Text?.Length ?? 0);
        var uploads = new List<(string Title, string Text)>();

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
                    var sb = new StringBuilder();
                    foreach (var page in pdf.GetPages())
                        sb.AppendLine(page.Text);
                    uploads.Add((file.FileName, sb.ToString()));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read PDF {File}", file.FileName);
                    return BadRequest($"Failed to read PDF '{file.FileName}': {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(form.Text))
            uploads.Add((form.Title ?? "document", form.Text));

        foreach (var (title, text) in uploads)
        {
            if (string.IsNullOrWhiteSpace(text))
                return BadRequest($"Extracted text for '{title}' is empty. Check your file or input.");
            try
            {
                await _store.IngestAsync(title, text);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to store document {Title}", title);
                return Problem(detail: ex.Message, statusCode: 502, title: $"Failed to store document '{title}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure storing document {Title}", title);
                return Problem(detail: ex.Message, statusCode: 500, title: $"Failed to store document '{title}'");
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
}
