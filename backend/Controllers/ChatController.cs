using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace backend.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly VectorStore _store;
    private readonly LlmClient _llm;
    private readonly ILogger<ChatController> _logger;
    private readonly IMemoryCache _cache;
    private readonly ChatOptions _options;
    private readonly StatsStore _stats;
    private readonly ConversationStore _conversations;

    public ChatController(VectorStore store, LlmClient llm, ILogger<ChatController> logger, IMemoryCache cache, IOptions<ChatOptions> options, StatsStore stats, ConversationStore conversations)
    {
        _store = store;
        _llm = llm;
        _logger = logger;
        _cache = cache;
        _options = options.Value;
        _stats = stats;
        _conversations = conversations;
    }

    [HttpPost("query")]
    public async Task<ActionResult<ChatResponse>> Query(ChatQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Query is required.");

        if (request.TopK <= 0)
            return BadRequest("TopK must be greater than 0.");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cooldown = _options.CooldownSeconds;
        if (cooldown > 0)
        {
            var now = DateTime.UtcNow;
            if (_cache.TryGetValue(ip, out DateTime last))
            {
                var diff = now - last;
                if (diff.TotalSeconds < cooldown)
                {
                    var wait = Math.Ceiling(cooldown - diff.TotalSeconds);
                    return Problem(detail: $"Please wait {wait} seconds before retrying.", statusCode: 429, title: "Cooldown active");
                }
            }
            _cache.Set(ip, now, TimeSpan.FromSeconds(cooldown));
        }

        _logger.LogInformation("Chat query '{Query}' with topK {TopK} and categories {Categories}", request.Query, request.TopK, request.CategoryIds);
        var conversationId = request.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversations.Exists(conversationId))
            conversationId = _conversations.CreateConversation();
        var history = _conversations.GetOrCreate(conversationId);
        List<(string Source, int? Page, string Content, double Score)> results;
        try
        {
            results = await _store.SearchAsync(request.Query, request.TopK, request.CategoryIds);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Search failed for query {Query}", request.Query);
            return Problem(detail: ex.Message, statusCode: 502, title: "Search failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected search failure for query {Query}", request.Query);
            return Problem(detail: ex.Message, statusCode: 500, title: "Search failed");
        }
        var enumerated = results.Select((r, idx) => (Index: idx + 1, r.Source, r.Page, r.Content, r.Score)).ToList();
        // Always let the LLM attempt an answer even when no relevant documents are found.
        // Provide a placeholder context so the model can still respond instead of returning
        // the "knowledge belum tersedia" message.
        var context = enumerated.Count > 0
            ? string.Join("\n", enumerated.Select(r =>
                r.Page.HasValue
                    ? $"[{r.Index}] {r.Source} (p.{r.Page})\n{r.Content}"
                    : $"[{r.Index}] {r.Source}\n{r.Content}"))
            : "No relevant context found in the knowledge base.";
        var prompt = new StringBuilder()
            .AppendLine("Use the following context to answer the question. Cite sources using [number] notation.")
            .AppendLine(context)
            .AppendLine($"Question: {request.Query}")
            .ToString();

        history.Add(new ChatMessage("user", prompt));

        string answer;
        try
        {
            answer = await _llm.GenerateAsync(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM generation failed for query {Query}", request.Query);
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }

        history.Add(new ChatMessage("assistant", answer));
        var sources = enumerated.Select(r => new Source
        {
            Index = r.Index,
            File = r.Source,
            Page = r.Page,
            Content = r.Content,
            Score = r.Score
        }).ToList();
        try
        {
            await _stats.LogChatAsync(request.Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log chat query {Query}", request.Query);
        }
        return new ChatResponse { Answer = answer, Sources = sources, LowConfidence = false, ConversationId = conversationId };
    }

    [HttpPost("stream")]
    public async Task Stream(ChatQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Query is required.");
            return;
        }

        if (request.TopK <= 0)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("TopK must be greater than 0.");
            return;
        }

        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Content-Type", "text/event-stream");
        Response.Headers.Add("X-Accel-Buffering", "no");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cooldown = _options.CooldownSeconds;
        if (cooldown > 0)
        {
            var now = DateTime.UtcNow;
            if (_cache.TryGetValue(ip, out DateTime last))
            {
                var diff = now - last;
                if (diff.TotalSeconds < cooldown)
                {
                    Response.StatusCode = 429;
                    await Response.WriteAsync($"Please wait {Math.Ceiling(cooldown - diff.TotalSeconds)} seconds before retrying.");
                    return;
                }
            }
            _cache.Set(ip, now, TimeSpan.FromSeconds(cooldown));
        }

        _logger.LogInformation("Chat stream '{Query}' with topK {TopK} and categories {Categories}", request.Query, request.TopK, request.CategoryIds);
        var conversationId = request.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversations.Exists(conversationId))
            conversationId = _conversations.CreateConversation();
        var history = _conversations.GetOrCreate(conversationId);
        List<(string Source, int? Page, string Content, double Score)> results;
        try
        {
            results = await _store.SearchAsync(request.Query, request.TopK, request.CategoryIds);
        }
        catch (InvalidOperationException ex)
        {
            Response.StatusCode = 502;
            await Response.WriteAsync($"Search failed: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            Response.StatusCode = 500;
            await Response.WriteAsync($"Search failed: {ex.Message}");
            return;
        }

        var enumerated = results.Select((r, idx) => (Index: idx + 1, r.Source, r.Page, r.Content, r.Score)).ToList();
        var context = enumerated.Count > 0
            ? string.Join("\n", enumerated.Select(r =>
                r.Page.HasValue
                    ? $"[{r.Index}] {r.Source} (p.{r.Page})\n{r.Content}"
                    : $"[{r.Index}] {r.Source}\n{r.Content}"))
            : "No relevant context found in the knowledge base.";
        var prompt = new StringBuilder()
            .AppendLine("Use the following context to answer the question. Cite sources using [number] notation.")
            .AppendLine(context)
            .AppendLine($"Question: {request.Query}")
            .ToString();

        history.Add(new ChatMessage("user", prompt));

        var sources = enumerated.Select(r => new Source
        {
            Index = r.Index,
            File = r.Source,
            Page = r.Page,
            Content = r.Content,
            Score = r.Score
        }).ToList();

        await Response.WriteAsync($"event: id\ndata: {conversationId}\n\n");
        await Response.WriteAsync($"event: sources\ndata: {JsonSerializer.Serialize(sources)}\n\n");
        await Response.Body.FlushAsync();

        var sb = new StringBuilder();

        try
        {
            await foreach (var token in _llm.GenerateStreamAsync(history))
            {
                var payload = JsonSerializer.Serialize(token);
                sb.Append(token);
                await Response.WriteAsync($"event: token\ndata: {payload}\n\n");
                await Response.Body.FlushAsync();
            }
            await Response.WriteAsync("event: done\ndata: end\n\n");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM streaming failed for query {Query}", request.Query);
            await Response.WriteAsync($"event: error\ndata: {ex.Message}\n\n");
        }

        history.Add(new ChatMessage("assistant", sb.ToString()));

        try
        {
            await _stats.LogChatAsync(request.Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log chat query {Query}", request.Query);
        }
    }
}

public class ChatQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public int[]? CategoryIds { get; set; }
    public string? ConversationId { get; set; }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Source> Sources { get; set; } = new();
    public bool LowConfidence { get; set; }
    public string ConversationId { get; set; } = string.Empty;
}

public class Source
{
    public int Index { get; set; }
    public string File { get; set; } = string.Empty;
    public int? Page { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
