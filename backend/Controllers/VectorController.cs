using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/vector")]
public class VectorController : ControllerBase
{
    private readonly VectorStore _store;

    public VectorController(VectorStore store)
    {
        _store = store;
    }

    [HttpPost("search")]
    public async Task<ActionResult<IEnumerable<SearchResult>>> Search(VectorSearchRequest request)
    {
        var results = await _store.SearchAsync(request.Query, request.TopK);
        return results.Select(r => new SearchResult { Content = r.Content, Score = r.Score }).ToList();
    }
}

public class VectorSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class SearchResult
{
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
