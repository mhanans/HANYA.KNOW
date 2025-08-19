using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using UglyToad.PdfPig;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestController : ControllerBase
{
    private readonly VectorStore _store;

    public IngestController(VectorStore store)
    {
        _store = store;
    }

    [HttpPost]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Post([FromForm] IngestForm form)
    {
        if (form.File == null && string.IsNullOrWhiteSpace(form.Text))
            return BadRequest("Provide a PDF file in 'file' or text in the 'text' field.");

        string text = form.Text ?? string.Empty;

        if (form.File != null)
        {
            if (!form.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Unsupported file type. Only PDF files are accepted.");

            try
            {
                await using var ms = new MemoryStream();
                await form.File.CopyToAsync(ms);
                ms.Position = 0;
                using var pdf = PdfDocument.Open(ms);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                text = sb.ToString();
            }
            catch (Exception ex)
            {
                return BadRequest($"Failed to read PDF: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("Extracted text is empty. Check your file or input.");

        try
        {
            await _store.IngestAsync(form.Title ?? form.File?.FileName ?? "document", text);
        }
        catch (Exception ex)
        {
            return Problem($"Failed to store document: {ex.Message}");
        }
        return Ok();
    }
}

public class IngestForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
}
