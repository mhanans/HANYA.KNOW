using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using backend.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace backend.Services;

public class KnowledgeBaseIngestionService : BackgroundService
{
    private readonly KnowledgeBaseStore _store;
    private readonly EmbeddingClient _embeddings;
    private readonly ILogger<KnowledgeBaseIngestionService> _logger;
    private readonly KnowledgeBaseOptions _options;
    private readonly Channel<KnowledgeBaseIngestionJob> _queue;

    private record KnowledgeBaseIngestionJob(int DocumentId);

    public KnowledgeBaseIngestionService(
        KnowledgeBaseStore store,
        EmbeddingClient embeddings,
        IOptions<KnowledgeBaseOptions> options,
        ILogger<KnowledgeBaseIngestionService> logger)
    {
        _store = store;
        _embeddings = embeddings;
        _logger = logger;
        _options = options.Value;
        _queue = Channel.CreateUnbounded<KnowledgeBaseIngestionJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task EnqueueAsync(int documentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Enqueuing knowledge base document {DocumentId} for processing", documentId);
        return _queue.Writer.WriteAsync(new KnowledgeBaseIngestionJob(documentId), cancellationToken).AsTask();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessDocumentAsync(job.DocumentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down gracefully.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing knowledge base document {DocumentId}", job.DocumentId);
                try
                {
                    await _store.MarkFailedAsync(job.DocumentId, ex.Message, CancellationToken.None);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Failed to mark document {DocumentId} as failed after exception", job.DocumentId);
                }
            }
        }
    }

    private async Task ProcessDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting processing for knowledge base document {DocumentId}", documentId);
        var document = await _store.GetDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} no longer exists. Skipping processing.", documentId);
            return;
        }

        if (!File.Exists(document.StoragePath))
        {
            await _store.MarkFailedAsync(documentId, "Stored file could not be found for processing.", cancellationToken);
            _logger.LogWarning("File {Path} for document {DocumentId} does not exist", document.StoragePath, documentId);
            return;
        }

        await _store.MarkProcessingAsync(documentId, cancellationToken);
        await _store.DeleteChunksAsync(documentId, cancellationToken);

        try
        {
            var (chunks, pageNumbers) = await ExtractChunksAsync(document.StoragePath, cancellationToken);
            if (chunks.Count == 0)
            {
                await _store.MarkFailedAsync(documentId, "No readable text was found in the document.", cancellationToken);
                return;
            }

            var chunkCount = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkText = chunks[i];
                if (string.IsNullOrWhiteSpace(chunkText))
                    continue;

                float[] embedding;
                try
                {
                    embedding = await _embeddings.EmbedAsync(chunkText);
                }
                catch (Exception ex)
                {
                    await _store.MarkFailedAsync(documentId, $"Embedding failed: {ex.Message}", cancellationToken);
                    _logger.LogError(ex, "Embedding failed for document {DocumentId} chunk {ChunkIndex}", documentId, i + 1);
                    return;
                }

                await _store.InsertChunkAsync(documentId, ++chunkCount, pageNumbers[i], chunkText, embedding, cancellationToken);
            }

            if (chunkCount == 0)
            {
                await _store.MarkFailedAsync(documentId, "No valid content chunks were generated for the document.", cancellationToken);
                return;
            }

            await _store.MarkIndexedAsync(documentId, chunkCount, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while processing document {DocumentId}", documentId);
            await _store.MarkFailedAsync(documentId, ex.Message, cancellationToken);
        }
    }

    private async Task<(List<string> Chunks, List<int?> PageNumbers)> ExtractChunksAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var pdf = PdfDocument.Open(stream);

        var aggregatedText = new StringBuilder();
        var pageBoundaries = new List<(int Page, int StartIndex)>();

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            pageBoundaries.Add((page.Number, aggregatedText.Length));
            aggregatedText.Append(text).Append("\n\n");
        }

        var combined = aggregatedText.ToString();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return (new List<string>(), new List<int?>());
        }

        #pragma warning disable SKEXP0050
        var lines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(combined, 40);
        var chunks = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(lines, _options.ChunkMaxTokens, _options.ChunkOverlapTokens);
        #pragma warning restore SKEXP0050

        var chunkList = new List<string>();
        var pageNumbers = new List<int?>();

        var searchIndex = 0;
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                continue;

            var index = combined.IndexOf(chunk, searchIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                index = combined.IndexOf(chunk.Trim(), StringComparison.Ordinal);
            }

            var page = pageBoundaries.LastOrDefault(p => p.StartIndex <= index);
            chunkList.Add(chunk);
            pageNumbers.Add(page == default ? null : page.Page);
            searchIndex = index >= 0 ? index + chunk.Length : searchIndex;
        }

        return (chunkList, pageNumbers);
    }
}
