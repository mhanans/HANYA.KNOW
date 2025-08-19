using backend.Services;
using Microsoft.AspNetCore.Mvc;

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
        string text = form.Text ?? string.Empty;
        if (form.File != null)
        {
            using var reader = new StreamReader(form.File.OpenReadStream());
            text = await reader.ReadToEndAsync();
        }
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("No text provided");
        await _store.IngestAsync(form.Title ?? form.File?.FileName ?? "document", text);
        return Ok();
    }
}

public class IngestForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
}
