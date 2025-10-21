using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Controllers;

[ApiController]
[Route("api/knowledge-base")]
[UiAuthorize("admin-presales-history")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly KnowledgeBaseStore _store;
    private readonly KnowledgeBaseIngestionService _ingestion;
    private readonly KnowledgeBaseOptions _options;
    private readonly ILogger<KnowledgeBaseController> _logger;

    public KnowledgeBaseController(
        KnowledgeBaseStore store,
        KnowledgeBaseIngestionService ingestion,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeBaseController> logger)
    {
        _store = store;
        _ingestion = ingestion;
        _options = options.Value;
        _logger = logger;
    }

[HttpGet("documents")]
public async Task<ActionResult<IEnumerable<KnowledgeBaseDocumentResponse>>> GetDocuments(CancellationToken cancellationToken)
    {
        var documents = await _store.ListDocumentsAsync(cancellationToken);
        return Ok(documents.Select(ToResponse));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload([FromForm] KnowledgeBaseUploadForm form, CancellationToken cancellationToken)
    {
        if (form.Files == null || form.Files.Count == 0)
        {
            return BadRequest("At least one PDF file must be provided.");
        }

        Dictionary<string, KnowledgeBaseUploadMetadata>? metadata = null;
        if (!string.IsNullOrWhiteSpace(form.Metadata))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<KnowledgeBaseUploadMetadata>>(form.Metadata!, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (items != null)
                {
                    metadata = items
                        .Where(m => !string.IsNullOrWhiteSpace(m.OriginalFileName))
                        .GroupBy(m => m.OriginalFileName!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse knowledge base metadata payload");
                return BadRequest("Metadata payload is invalid JSON.");
            }
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? uploadedBy = int.TryParse(userId, out var parsedUserId) ? parsedUserId : null;

        foreach (var file in form.Files)
        {
            if (file == null)
                continue;

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"Unsupported file type for '{file.FileName}'. Only .pdf files are accepted.");
            }

            if (file.Length == 0)
            {
                return BadRequest($"The file '{file.FileName}' is empty.");
            }
        }

        var createdDocuments = new List<KnowledgeBaseDocument>();
        foreach (var file in form.Files)
        {
            if (file == null)
                continue;

            var meta = metadata != null && metadata.TryGetValue(file.FileName, out var found) ? found : null;
            var storagePath = await SaveFileAsync(file, cancellationToken);

            try
            {
                var document = new KnowledgeBaseDocument
                {
                    OriginalFileName = file.FileName,
                    StoragePath = storagePath,
                    ProjectName = meta?.ProjectName,
                    DocumentType = meta?.DocumentType ?? meta?.ClientType,
                    ClientType = meta?.ClientType,
                    ProjectCompletionDate = meta?.ProjectCompletionDate,
                    ProcessingStatus = KnowledgeBaseDocumentStatus.Pending,
                    UploadedByUserId = uploadedBy,
                    UploadedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var documentId = await _store.CreateDocumentAsync(document, cancellationToken);
                document.Id = documentId;
                createdDocuments.Add(document);
                await _ingestion.EnqueueAsync(documentId, cancellationToken);
            }
            catch (Exception ex)
            {
                TryDeleteFile(storagePath);
                _logger.LogError(ex, "Failed to enqueue knowledge base document {File}", file.FileName);
                throw;
            }
        }

        return Accepted(new
        {
            documents = createdDocuments.Select(ToResponse)
        });
    }

    [HttpDelete("documents/{id:int}")]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
    {
        var document = await _store.GetDocumentAsync(id, cancellationToken);
        if (document == null)
        {
            return NotFound();
        }

        await _store.DeleteDocumentAsync(id, cancellationToken);
        TryDeleteFile(document.StoragePath);
        return NoContent();
    }

    [HttpPost("documents/{id:int}/reprocess")]
    public async Task<IActionResult> ReprocessDocument(int id, CancellationToken cancellationToken)
    {
        var document = await _store.GetDocumentAsync(id, cancellationToken);
        if (document == null)
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(document.StoragePath))
        {
            return Problem(detail: "The stored file could not be found for re-processing.", statusCode: StatusCodes.Status409Conflict);
        }

        await _store.MarkProcessingAsync(id, cancellationToken);
        await _ingestion.EnqueueAsync(id, cancellationToken);
        return Accepted();
    }

    private string ResolveStoragePath()
    {
        var basePath = string.IsNullOrWhiteSpace(_options.StoragePath)
            ? "knowledge-base"
            : _options.StoragePath;

        if (!Path.IsPathRooted(basePath))
        {
            basePath = Path.Combine(AppContext.BaseDirectory, basePath);
        }

        Directory.CreateDirectory(basePath);
        return basePath;
    }

    private async Task<string> SaveFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var directory = ResolveStoragePath();
        var fileName = $"{Guid.NewGuid():N}-{SanitizeFileName(file.FileName)}";
        var path = Path.Combine(directory, fileName);
        await using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream, cancellationToken);
        return path;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized;
    }

    private static KnowledgeBaseDocumentResponse ToResponse(KnowledgeBaseDocument document)
    {
        return new KnowledgeBaseDocumentResponse
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ProjectName = document.ProjectName,
            DocumentType = document.DocumentType ?? document.ClientType,
            ProcessingStatus = document.ProcessingStatus,
            ErrorMessage = document.ErrorMessage,
            UploadedAt = document.UploadedAt,
            ProcessedAt = document.ProcessedAt,
            UpdatedAt = document.UpdatedAt,
            ChunkCount = document.ChunkCount
        };
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete stored file {Path} after document removal", path);
        }
    }
}

public class KnowledgeBaseUploadForm
{
    public List<IFormFile>? Files { get; set; }
    public string? Metadata { get; set; }
}

public class KnowledgeBaseUploadMetadata
{
    public string? OriginalFileName { get; set; }
    public string? ProjectName { get; set; }
    public string? DocumentType { get; set; }
    public string? ClientType { get; set; }
    public DateTime? ProjectCompletionDate { get; set; }
}

public class KnowledgeBaseDocumentResponse
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? DocumentType { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? ChunkCount { get; set; }
}
