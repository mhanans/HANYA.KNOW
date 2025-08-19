using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace backend.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationController : ControllerBase
{
    private readonly VectorStore _store;
    private readonly LlmClient _llm;
    private readonly RecommendationStore _recStore;
    private readonly RecommendationOptions _options;

    public RecommendationController(VectorStore store, LlmClient llm, RecommendationStore recStore, IOptions<RecommendationOptions> options)
    {
        _store = store;
        _llm = llm;
        _recStore = recStore;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<IReadOnlyList<CvRecommendation>> List() => await _recStore.ListAsync();

    [HttpPost]
    public async Task<ActionResult<CvRecommendation>> Create(RecommendationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Position) || string.IsNullOrWhiteSpace(request.Details))
            return BadRequest("Position and details are required.");

        string summary;
        try
        {
            summary = await GenerateAsync(request.Position, request.Details);
        }
        catch (Exception ex)
        {
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }
        var rec = await _recStore.AddAsync(request.Position, request.Details, summary);
        return rec;
    }

    [HttpPost("{id}/retry")]
    public async Task<ActionResult<CvRecommendation>> Retry(int id)
    {
        var existing = await _recStore.GetAsync(id);
        if (existing == null) return NotFound();

        string summary;
        try
        {
            summary = await GenerateAsync(existing.Position, existing.Details);
        }
        catch (Exception ex)
        {
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Generation failed");
        }
        var updated = await _recStore.UpdateSummaryAsync(id, summary);
        return updated;
    }

    private async Task<string> GenerateAsync(string position, string details)
    {
        var query = $"{position}\n{details}";
        var results = await _store.SearchAsync(query, 10, new[] { _options.CvCategoryId });
        var context = string.Join("\n", results.Select((r, i) =>
            r.Page.HasValue ? $"[{i + 1}] {r.Source} (p.{r.Page})\n{r.Content}" : $"[{i + 1}] {r.Source}\n{r.Content}"));
        var prompt = new StringBuilder()
            .AppendLine("You are a senior HR specialist. Based on the CVs below, recommend the top 3 candidates for the given position.")
            .AppendLine($"Position: {position}")
            .AppendLine($"Details: {details}")
            .AppendLine("CVs:")
            .AppendLine(context)
            .AppendLine("Provide a numbered list of the top 3 candidates with short reasons.")
            .ToString();
        return await _llm.GenerateAsync(prompt);
    }
}

public class RecommendationRequest
{
    public string Position { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
