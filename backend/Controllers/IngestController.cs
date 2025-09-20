using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using Microsoft.Extensions.Logging;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[UiAuthorize("upload")]
public class IngestController : ControllerBase
{
    private readonly VectorStore _store;
    private readonly ILogger<IngestController> _logger;

    private record PageContentWithIndex(int PageNumber, int StartIndex, string Text);

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

                    // Step 1: Build aggregated text AND map the start index of each page
                    var pageContents = new List<PageContentWithIndex>();
                    var aggregatedTextBuilder = new StringBuilder();
                    foreach (var page in pdf.GetPages())
                    {
                        var pageText = page.Text;
                        if (string.IsNullOrWhiteSpace(pageText)) continue;

                        pageContents.Add(new PageContentWithIndex(
                            PageNumber: page.Number,
                            StartIndex: aggregatedTextBuilder.Length,
                            Text: pageText
                        ));
                        aggregatedTextBuilder.Append(pageText).Append("\n\n");
                    }

                    var aggregatedText = aggregatedTextBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(aggregatedText)) continue;

                    // Step 2: Perform semantic chunking on the aggregated text
                    #pragma warning disable SKEXP0050 // Acknowledge experimental TextChunker API
                    var lines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(aggregatedText, 40);
                    var chunks = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(lines, 1000, 100);
                    #pragma warning restore SKEXP0050

                    // Step 3 & 4: Iterate chunks, find their original page, and ingest
                    int currentSearchIndex = 0;
                    foreach (var chunk in chunks)
                    {
                        if (string.IsNullOrWhiteSpace(chunk)) continue;

                        int chunkStartIndex = aggregatedText.IndexOf(chunk, currentSearchIndex, StringComparison.Ordinal);
                        if (chunkStartIndex == -1)
                        {
                            _logger.LogWarning("Could not find chunk in aggregated text. Skipping. Chunk: {ChunkSnippet}", chunk.Substring(0, Math.Min(50, chunk.Length)));
                            continue;
                        }

                        // Find the most recent page whose start index is before or at the chunk's start index
                        var pageInfo = pageContents.LastOrDefault(p => p.StartIndex <= chunkStartIndex);
                        int? pageNumber = pageInfo?.PageNumber;

                        try
                        {
                            await _store.IngestAsync(file.FileName, pageNumber, chunk, form.CategoryId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to store chunk for document {File} from page {Page}", file.FileName, pageNumber);
                            return Problem(detail: ex.Message, statusCode: 502, title: $"Failed to store chunk of document '{file.FileName}'");
                        }

                        // Advance search position to handle potential duplicate chunks
                        currentSearchIndex = chunkStartIndex + chunk.Length;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process PDF {File}", file.FileName);
                    return BadRequest($"Failed to process PDF '{file.FileName}': {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(form.Text))
        {
            var title = string.IsNullOrWhiteSpace(form.Title)
                ? (form.Text.Length > 30 ? form.Text[..30] + "..." : form.Text)
                : form.Title;
            #pragma warning disable SKEXP0050 // Acknowledge experimental TextChunker API
            var lines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(form.Text, 40);
            var chunks = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(lines, 1000, 100);
            #pragma warning restore SKEXP0050

            int chunkIdx = 1;
            foreach (var chunk in chunks)
            {
                if (string.IsNullOrWhiteSpace(chunk)) continue;
                try
                {
                    await _store.IngestAsync(title, null, chunk, form.CategoryId);
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
