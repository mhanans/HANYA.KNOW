using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class SourceCodeSyncService
{
    private readonly string _connectionString;
    private readonly SourceCodeOptions _options;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<SourceCodeSyncService> _logger;
    private readonly IHostEnvironment _environment;
    private readonly int _expectedDimensions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Guid? _currentJobId;
    private DateTimeOffset? _currentStartedAt;

    public SourceCodeSyncService(
        IOptions<PostgresOptions> dbOptions,
        IOptions<SourceCodeOptions> options,
        IOptions<EmbeddingOptions> embeddingOptions,
        EmbeddingClient embeddingClient,
        ILogger<SourceCodeSyncService> logger,
        IHostEnvironment environment)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _options = options.Value;
        _embeddingClient = embeddingClient;
        _logger = logger;
        _environment = environment;
        _expectedDimensions = embeddingOptions.Value.Dimensions;
    }

    public async Task<SourceCodeSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var running = _currentJobId.HasValue;
        var status = new SourceCodeSyncStatus
        {
            IsRunning = running,
            ActiveJobId = _currentJobId,
            ActiveJobStartedAt = _currentStartedAt
        };

        if (string.IsNullOrWhiteSpace(_connectionString))
            return status;

        const string sql = @"SELECT id, started_at, completed_at, status, details, file_count, chunk_count, duration_seconds
                               FROM code_sync_jobs
                               ORDER BY started_at DESC
                               LIMIT 1";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return status;

        status = status with
        {
            LastJobId = reader.GetGuid(0),
            LastStartedAt = ToDateTimeOffset(reader.GetDateTime(1)),
            LastCompletedAt = reader.IsDBNull(2) ? null : ToDateTimeOffset(reader.GetDateTime(2)),
            LastStatus = reader.IsDBNull(3) ? null : reader.GetString(3),
            LastError = reader.IsDBNull(4) ? null : reader.GetString(4),
            LastFileCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            LastChunkCount = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            LastDurationSeconds = reader.IsDBNull(7) ? null : reader.GetDouble(7)
        };

        return status;
    }

    public async Task<SourceCodeSyncStatus> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("Postgres connection string is not configured.");

        if (!await _gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A source code sync is already running.");

        var startedAt = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        _currentJobId = jobId;
        _currentStartedAt = startedAt;

        try
        {
            var root = ResolveSourceRoot();
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                _logger.LogWarning("Source code directory {Root} did not exist. Created the directory but there are no files to ingest.", root);
            }

            var includeExtensions = (_options.IncludeExtensions?.Length ?? 0) > 0
                ? new HashSet<string>(_options.IncludeExtensions.Select(e => e.StartsWith('.') ? e : "." + e), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { ".cs", ".cshtml", ".ts", ".tsx", ".js", ".jsx", ".py", ".java" }, StringComparer.OrdinalIgnoreCase);
            var excludeDirectories = (_options.ExcludeDirectories?.Length ?? 0) > 0
                ? new HashSet<string>(_options.ExcludeDirectories, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { ".git", "node_modules", "bin", "obj", "dist", "build" }, StringComparer.OrdinalIgnoreCase);

            var files = EnumerateFiles(root, includeExtensions, excludeDirectories).ToList();
            _logger.LogInformation("Discovered {Count} source files for ingestion under {Root}", files.Count, root);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            await InsertJobAsync(conn, tx, jobId, startedAt);

            var totalFiles = 0;
            var totalChunks = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = NormalizePath(Path.GetRelativePath(root, file));
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var chunks = ChunkFile(content, relativePath);

                await DeleteExistingEmbeddingsAsync(conn, tx, relativePath, cancellationToken);

                foreach (var chunk in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var embedding = await _embeddingClient.EmbedAsync(chunk.Content);
                    if (embedding == null || embedding.Length == 0)
                        throw new InvalidOperationException($"Embedding service returned an empty vector for {relativePath}.");
                    if (embedding.Length != _expectedDimensions)
                        throw new InvalidOperationException($"Embedding dimension mismatch. Expected {_expectedDimensions} but got {embedding.Length}.");

                    await InsertEmbeddingAsync(conn, tx, relativePath, chunk, embedding, cancellationToken);
                    totalChunks++;
                }

                totalFiles++;
            }

            var completedAt = DateTimeOffset.UtcNow;
            var duration = (completedAt - startedAt).TotalSeconds;

            await UpdateJobAsync(conn, tx, jobId, completedAt, "completed", null, totalFiles, totalChunks, duration, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Source code sync completed successfully in {Seconds:F1}s ({Files} files, {Chunks} chunks)", duration, totalFiles, totalChunks);
            return await GetStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source code sync failed");
            await PersistFailureAsync(ex, cancellationToken);
            throw;
        }
        finally
        {
            _currentJobId = null;
            _currentStartedAt = null;
            _gate.Release();
        }
    }

    private async Task PersistFailureAsync(Exception ex, CancellationToken cancellationToken)
    {
        if (!_currentJobId.HasValue || string.IsNullOrWhiteSpace(_connectionString))
            return;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            const string sql = @"UPDATE code_sync_jobs
                                   SET completed_at = @completed,
                                       status = 'failed',
                                       details = @details
                                   WHERE id = @id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("completed", ToUtcDateTime(DateTimeOffset.UtcNow));
            cmd.Parameters.AddWithValue("details", ex.Message);
            cmd.Parameters.AddWithValue("id", _currentJobId.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception persistEx)
        {
            _logger.LogError(persistEx, "Failed to persist failure status for source code sync");
        }
    }

    private async Task InsertJobAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid jobId, DateTimeOffset startedAt)
    {
        const string sql = @"INSERT INTO code_sync_jobs (id, started_at, status)
                               VALUES (@id, @started, 'running')";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", jobId);
        cmd.Parameters.AddWithValue("started", ToUtcDateTime(startedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateJobAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid jobId,
        DateTimeOffset completedAt,
        string status,
        string? details,
        int fileCount,
        int chunkCount,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        const string sql = @"UPDATE code_sync_jobs
                               SET completed_at = @completed,
                                   status = @status,
                                   details = @details,
                                   file_count = @files,
                                   chunk_count = @chunks,
                                   duration_seconds = @duration
                               WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("completed", ToUtcDateTime(completedAt));
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("details", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("files", fileCount);
        cmd.Parameters.AddWithValue("chunks", chunkCount);
        cmd.Parameters.AddWithValue("duration", durationSeconds);
        cmd.Parameters.AddWithValue("id", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteExistingEmbeddingsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string relativePath, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM code_embeddings WHERE file_path = @path";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("path", relativePath);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertEmbeddingAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string filePath,
        CodeChunk chunk,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO code_embeddings
                               (file_path, symbol_name, content, start_line, end_line, checksum, embedding)
                               VALUES (@file, @symbol, @content, @start, @end, @checksum, @embedding::vector)";

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("file", filePath);
        cmd.Parameters.AddWithValue("symbol", (object?)chunk.SymbolName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("start", chunk.StartLine);
        cmd.Parameters.AddWithValue("end", chunk.EndLine);
        cmd.Parameters.AddWithValue("checksum", chunk.Checksum);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters["embedding"].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Real;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private IEnumerable<string> EnumerateFiles(string root, HashSet<string> includeExtensions, HashSet<string> excludeDirectories)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (excludeDirectories.Contains(name))
                    continue;
                stack.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                var ext = Path.GetExtension(file);
                if (includeExtensions.Count == 0 || includeExtensions.Contains(ext))
                    yield return file;
            }
        }
    }

    private IEnumerable<CodeChunk> ChunkFile(string content, string relativePath)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var chunkSize = Math.Max(1, _options.ChunkSize);
        var overlap = Math.Clamp(_options.ChunkOverlap, 0, chunkSize - 1);
        var step = Math.Max(1, chunkSize - overlap);

        var index = 0;
        while (index < lines.Length)
        {
            var end = Math.Min(lines.Length, index + chunkSize);
            var segment = lines[index..end];
            var text = string.Join("\n", segment).TrimEnd();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var startLine = index + 1;
                var endLine = end;
                yield return new CodeChunk(relativePath, null, text, startLine, endLine, ComputeChecksum(text));
            }

            if (end >= lines.Length)
                break;

            index += step;
        }
    }

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');

    private string ResolveSourceRoot()
    {
        if (string.IsNullOrWhiteSpace(_options.SourceDirectory))
            return Path.Combine(_environment.ContentRootPath, "source-code");

        if (Path.IsPathRooted(_options.SourceDirectory))
            return _options.SourceDirectory;

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.SourceDirectory));
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }

    private static DateTime ToUtcDateTime(DateTimeOffset value)
        => value.UtcDateTime;

    private record CodeChunk(string FilePath, string? SymbolName, string Content, int StartLine, int EndLine, string Checksum);
}

public record SourceCodeSyncStatus
{
    public bool IsRunning { get; init; }
    public Guid? ActiveJobId { get; init; }
    public DateTimeOffset? ActiveJobStartedAt { get; init; }
    public Guid? LastJobId { get; init; }
    public DateTimeOffset? LastStartedAt { get; init; }
    public DateTimeOffset? LastCompletedAt { get; init; }
    public string? LastStatus { get; init; }
    public string? LastError { get; init; }
    public int? LastFileCount { get; init; }
    public int? LastChunkCount { get; init; }
    public double? LastDurationSeconds { get; init; }
}
