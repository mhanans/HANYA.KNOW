using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly VectorStore _store;

    public ChatController(VectorStore store)
    {
        _store = store;
    }

    [HttpPost("query")]
    public async Task<ActionResult<ChatResponse>> Query(ChatQueryRequest request)
    {
        var results = await _store.SearchAsync(request.Query, request.TopK);
        var answer = string.Join("\n", results.Select(r => r.Content));
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
