using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Linq;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/chat")]
[UiAuthorize("chat")]
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

        var (errorResult, ragPayload) = await PrepareRagPayloadAsync(request);
        if (errorResult != null || ragPayload == null)
        {
            if (errorResult != null)
            {
                return errorResult;
            }
            return Problem("Failed to prepare RAG payload.", statusCode: 500);
        }

        string answer;
        try
        {
            answer = await _llm.GenerateAsync(ragPayload.History);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM generation failed for query {Query}", request.Query);
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }

        ragPayload.History.Add(new ChatMessage("assistant", answer));

        try
        {
            await _stats.LogChatAsync(request.Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log chat query {Query}", request.Query);
        }

        var isLowConfidence = !ragPayload.Sources.Any();
        return new ChatResponse
        {
            Answer = answer,
            Sources = ragPayload.Sources,
            LowConfidence = isLowConfidence,
            ConversationId = ragPayload.ConversationId
        };
    }

    [HttpGet("stream")]
    public async Task Stream([FromQuery] ChatQueryRequest request)
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

        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (errorResult, ragPayload) = await PrepareRagPayloadAsync(request);
        if (errorResult != null || ragPayload == null)
        {
            Response.StatusCode = (errorResult as ObjectResult)?.StatusCode ?? 500;
            var problemDetails = errorResult as ObjectResult;
            await Response.WriteAsync(JsonSerializer.Serialize(problemDetails?.Value ?? new { message = "An error occurred." }));
            return;
        }

        await Response.WriteAsync($"event: id\ndata: {ragPayload.ConversationId}\n\n");
        await Response.WriteAsync($"event: sources\ndata: {JsonSerializer.Serialize(ragPayload.Sources)}\n\n");
        await Response.Body.FlushAsync();

        var sb = new StringBuilder();

        try
        {
            await foreach (var token in _llm.GenerateStreamAsync(ragPayload.History))
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

        ragPayload.History.Add(new ChatMessage("assistant", sb.ToString()));

        try
        {
            await _stats.LogChatAsync(request.Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log chat query {Query}", request.Query);
        }
    }

    private async Task<(ActionResult? Error, RagPayload? Payload)> PrepareRagPayloadAsync(ChatQueryRequest request)
    {
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
                    return (Problem(detail: $"Please wait {wait} seconds before retrying.", statusCode: 429, title: "Cooldown active"), null);
                }
            }
            _cache.Set(ip, now, TimeSpan.FromSeconds(cooldown));
        }

        _logger.LogInformation("Processing chat query '{Query}' with topK {TopK} and categories {Categories}", request.Query, request.TopK, request.CategoryIds);

        var conversationId = request.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId) || !_conversations.Exists(conversationId))
            conversationId = _conversations.CreateConversation();
        var history = _conversations.GetOrCreate(conversationId);

        List<(string Source, int? Page, string Content, double Score)> results;
        try
        {
            results = await _store.SearchAsync(request.Query, request.TopK, request.CategoryIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query {Query}", request.Query);
            return (Problem(detail: ex.Message, statusCode: 502, title: "Search failed"), null);
        }

        var scoreThreshold = _options.ScoreThreshold;
        var relevantResults = results.Where(r => r.Score >= scoreThreshold).ToList();
        var enumerated = relevantResults.Select((r, idx) => (Index: idx + 1, r.Source, r.Page, r.Content, r.Score)).ToList();

        var context = enumerated.Any()
            ? string.Join("\n", enumerated.Select(r =>
                r.Page.HasValue
                    ? $"[{r.Index}] {r.Source} (p.{r.Page})\n{r.Content}"
                    : $"[{r.Index}] {r.Source}\n{r.Content}"))
            : "No relevant documents were found in the knowledge base that met the confidence threshold.";

        const string fallbackPromptTemplate = "You are a helpful assistant. Answer the user's question based ONLY on the provided context below. Do not use any external knowledge. If the context does not contain the information to answer, you must state 'Berdasarkan informasi yang tersedia, saya tidak dapat menemukan jawaban untuk pertanyaan tersebut.'. Cite sources using [number] notation. Answer in Indonesian.\n\n--- CONTEXT START ---\n{context}\n--- CONTEXT END ---\n\nQuestion: {query}";
        var promptTemplate = string.IsNullOrWhiteSpace(_options.PromptTemplate)
            ? fallbackPromptTemplate
            : _options.PromptTemplate;

        var prompt = promptTemplate
            .Replace("{context}", context)
            .Replace("{query}", request.Query);

        history.Add(new ChatMessage("user", prompt));

        var sources = enumerated.Select(r => new Source
        {
            Index = r.Index,
            File = r.Source,
            Page = r.Page,
            Content = r.Content,
            Score = r.Score
        }).ToList();

        var payload = new RagPayload(conversationId, history, sources);
        return (null, payload);
    }

    [HttpGet("history")]
    public ActionResult<IEnumerable<ConversationInfo>> History()
    {
        var items = _conversations.GetHistory()
            .Select(h => new ConversationInfo(h.Id, h.Created, h.FirstMessage ?? string.Empty))
            .OrderByDescending(h => h.Created);
        return Ok(items);
    }

    [HttpGet("history/{id}")]
    public ActionResult<IEnumerable<ChatMessage>> GetConversation(string id)
    {
        var convo = _conversations.GetConversation(id);
        if (convo == null) return NotFound();
        return Ok(convo);
    }

    private record RagPayload(string ConversationId, List<ChatMessage> History, List<Source> Sources);
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

public record ConversationInfo(string Id, DateTime Created, string FirstMessage);
