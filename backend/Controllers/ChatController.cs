using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

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

    public ChatController(VectorStore store, LlmClient llm, ILogger<ChatController> logger, IMemoryCache cache, IOptions<ChatOptions> options)
    {
        _store = store;
        _llm = llm;
        _logger = logger;
        _cache = cache;
        _options = options.Value;
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
        var context = string.Join("\n", enumerated.Select(r =>
            r.Page.HasValue ? $"[{r.Index}] {r.Source} (p.{r.Page})\n{r.Content}" : $"[{r.Index}] {r.Source}\n{r.Content}"));
        var prompt = new StringBuilder()
            .AppendLine("Use the following context to answer the question. Cite sources using [number] notation.")
            .AppendLine(context)
            .AppendLine($"Question: {request.Query}")
            .ToString();

        string answer;
        try
        {
            answer = await _llm.GenerateAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM generation failed for query {Query}", request.Query);
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }
        var sources = enumerated.Select(r => new Source
        {
            Index = r.Index,
            File = r.Source,
            Page = r.Page,
            Content = r.Content,
            Score = r.Score
        }).ToList();
        var lowConfidence = sources.Count == 0 || sources[0].Score < 0.5;
        return new ChatResponse { Answer = answer, Sources = sources, LowConfidence = lowConfidence };
    }
}

public class ChatQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public int[]? CategoryIds { get; set; }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Source> Sources { get; set; } = new();
    public bool LowConfidence { get; set; }
}

public class Source
{
    public int Index { get; set; }
    public string File { get; set; } = string.Empty;
    public int? Page { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
