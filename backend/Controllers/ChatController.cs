using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Collections.Generic;

namespace backend.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly VectorStore _store;
    private readonly LlmClient _llm;

    public ChatController(VectorStore store, LlmClient llm)
    {
        _store = store;
        _llm = llm;
    }

    [HttpPost("query")]
    public async Task<ActionResult<ChatResponse>> Query(ChatQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("Query is required.");

        if (request.TopK <= 0)
            return BadRequest("TopK must be greater than 0.");

        List<(string Title, string Content, double Score)> results;
        try
        {
            results = await _store.SearchAsync(request.Query, request.TopK);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: 502, title: "Search failed");
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500, title: "Search failed");
        }
        var context = string.Join("\n", results.Select(r => $"[{r.Title}] {r.Content}"));
        var prompt = new StringBuilder()
            .AppendLine("Use the following context to answer the question with citations.")
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
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }
        var sources = results.Select(r => new Source
        {
            Title = r.Title,
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
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Source> Sources { get; set; } = new();
    public bool LowConfidence { get; set; }
}

public class Source
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
