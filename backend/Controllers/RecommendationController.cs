using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

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

        string summaryJson = string.Empty;
        try { summaryJson = await SummarizeAsync(summary); }
        catch { /* ignore summarization failures */ }

        var rec = await _recStore.AddAsync(request.Position, request.Details, summary, summaryJson);
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

        string summaryJson = string.Empty;
        try { summaryJson = await SummarizeAsync(summary); }
        catch { /* ignore */ }

        var updated = await _recStore.UpdateAsync(id, summary, summaryJson);
        return updated;
    }

    [HttpPost("{id}/retry-summary")]
    public async Task<ActionResult<CvRecommendation>> RetrySummary(int id)
    {
        var existing = await _recStore.GetAsync(id);
        if (existing == null) return NotFound();

        string summaryJson;
        try
        {
            summaryJson = await SummarizeAsync(existing.Summary);
        }
        catch (Exception ex)
        {
            return Problem(detail: $"LLM call failed: {ex.Message}", statusCode: 502, title: "Summary failed");
        }

        var updated = await _recStore.UpdateSummaryJsonAsync(id, summaryJson);
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
            .ToString();
        var raw = await _llm.GenerateAsync(prompt);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("LLM response was not a JSON array.");
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("Gemini response missing structured candidates", ex);
        }
    }

    private async Task<string> SummarizeAsync(string raw)
    {
        var prompt = new StringBuilder()
            .AppendLine("Convert the following recommendation into a JSON array of three objects with 'name' and 'reason' fields.")
            .AppendLine("If not possible, return an empty array.")
            .AppendLine(raw)
            .ToString();
        var json = await _llm.GenerateAsync(prompt);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("LLM summary was not a JSON array.");
        return JsonSerializer.Serialize(doc.RootElement);
    }

    private async Task<string> SummarizeAsync(string raw)
    {
        var prompt = new StringBuilder()
            .AppendLine("Convert the following recommendation into a JSON array of three objects with 'name' and 'reason' fields.")
            .AppendLine("If not possible, return an empty array.")
            .AppendLine(raw)
            .ToString();
        var json = await _llm.GenerateAsync(prompt);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("LLM summary was not a JSON array.");
        return JsonSerializer.Serialize(doc.RootElement);
    }
}

public class RecommendationRequest
{
    public string Position { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
