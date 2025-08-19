using backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;

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

        var results = await _store.SearchAsync(request.Query, request.TopK);
        var context = string.Join("\n", results.Select(r => r.Content));
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
            return Problem($"LLM call failed: {ex.Message}");
        }

        var citations = results.Select(r => r.Content).ToList();
        return new ChatResponse { Answer = answer, Sources = citations };
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
    public List<string> Sources { get; set; } = new();
}
