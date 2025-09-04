using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;

namespace backend.Services;

public class TicketAssignmentService
{
    private readonly TicketCategoryStore _categories;
    private readonly PicStore _pics;
    private readonly TicketStore _tickets;
    private readonly LlmClient _llm;
    private readonly TicketAssignmentResultStore _results;

    public TicketAssignmentService(
        TicketCategoryStore categories,
        PicStore pics,
        TicketStore tickets,
        LlmClient llm,
        TicketAssignmentResultStore results)
    {
        _categories = categories;
        _pics = pics;
        _tickets = tickets;
        _llm = llm;
        _results = results;
    }

    public async Task<(int? categoryId, int? picId, string? reason)> AutoAssignAsync(Ticket ticket)
    {
        var categories = await _categories.ListAsync();
        var pics = await _pics.ListAsync();

        var catLines = string.Join("\n", categories.Select(c => $"{c.Id}: {c.TicketType} - {c.Description} (sample: {c.SampleJson})"));
        var picLines = string.Join("\n", pics.Select(p => $"{p.Id}: {p.Name} handles [{string.Join(",", p.CategoryIds)}] is {(p.Availability ? "available" : "unavailable")} and has {p.TicketCount} tickets"));

        var prompt = $@"You are a support ticket router. Choose the best matching ticket category and an available PIC. Prefer PICs with fewer active tickets unless only one PIC handles the category.
Ticket categories:
{catLines}
PICs:
{picLines}
Ticket:
Number: {ticket.TicketNumber}
Complaint: {ticket.Complaint}
Detail: {ticket.Detail}";

        int? categoryId = null;
        int? picId = null;
        string? reason = null;

        string raw;
        string summaryJson = string.Empty;
        try
        {
            raw = await _llm.GenerateAsync(prompt);
            try { summaryJson = await SummarizeAsync(raw); } catch { }
            await _results.AddAsync(ticket.Id, raw, summaryJson);

            using var doc = JsonDocument.Parse(summaryJson);

            if (doc.RootElement.TryGetProperty("categoryId", out var catElem) && catElem.ValueKind == JsonValueKind.Number)
                categoryId = catElem.GetInt32();

            if (doc.RootElement.TryGetProperty("picId", out var picElem) && picElem.ValueKind == JsonValueKind.Number)
                picId = picElem.GetInt32();

            if (categoryId == null || !categories.Any(c => c.Id == categoryId))
            {
                reason = "AI returned unknown category";
                categoryId = null;
                picId = null;
            }
            else if (picId == null)
            {
                reason = "AI did not select a PIC";
                picId = null;
            }
            else
            {
                var pic = pics.FirstOrDefault(p => p.Id == picId);
                if (pic == null)
                {
                    reason = "AI returned unknown PIC";
                    picId = null;
                }
                else if (!pic.Availability)
                {
                    reason = $"PIC {pic.Name} unavailable";
                    picId = null;
                }
                else if (!pic.CategoryIds.Contains(categoryId.Value))
                {
                    reason = $"PIC {pic.Name} cannot handle category {categoryId}";
                    picId = null;
                }
                else
                {
                    var same = pics.Where(p => p.Availability && p.CategoryIds.Contains(categoryId.Value)).ToList();
                    if (same.Count > 1 && pic.TicketCount > same.Min(p => p.TicketCount))
                    {
                        reason = $"PIC {pic.Name} already has many tickets";
                        picId = null;
                    }
                }
            }
        }
        catch
        {
            reason = "AI assignment failed";
            categoryId = null;
            picId = null;
        }

        await _tickets.AssignAsync(ticket.Id, categoryId, picId, reason);
        return (categoryId, picId, reason);
    }

    public async Task<(int? categoryId, int? picId, string? reason)?> RetrySummaryAsync(int ticketId)
    {
        var existing = await _results.GetLatestAsync(ticketId);
        if (existing == null) return null;

        var categories = await _categories.ListAsync();
        var pics = await _pics.ListAsync();

        string summaryJson = await SummarizeAsync(existing.Response);
        await _results.UpdateJsonAsync(existing.Id, summaryJson);

        int? categoryId = null;
        int? picId = null;
        string? reason = null;

        using var doc = JsonDocument.Parse(summaryJson);

        if (doc.RootElement.TryGetProperty("categoryId", out var catElem) && catElem.ValueKind == JsonValueKind.Number)
            categoryId = catElem.GetInt32();

        if (doc.RootElement.TryGetProperty("picId", out var picElem) && picElem.ValueKind == JsonValueKind.Number)
            picId = picElem.GetInt32();

        if (categoryId == null || !categories.Any(c => c.Id == categoryId))
        {
            reason = "AI returned unknown category";
            categoryId = null;
            picId = null;
        }
        else if (picId == null)
        {
            reason = "AI did not select a PIC";
            picId = null;
        }
        else
        {
            var pic = pics.FirstOrDefault(p => p.Id == picId);
            if (pic == null)
            {
                reason = "AI returned unknown PIC";
                picId = null;
            }
            else if (!pic.Availability)
            {
                reason = $"PIC {pic.Name} unavailable";
                picId = null;
            }
            else if (!pic.CategoryIds.Contains(categoryId.Value))
            {
                reason = $"PIC {pic.Name} cannot handle category {categoryId}";
                picId = null;
            }
            else
            {
                var same = pics.Where(p => p.Availability && p.CategoryIds.Contains(categoryId.Value)).ToList();
                if (same.Count > 1 && pic.TicketCount > same.Min(p => p.TicketCount))
                {
                    reason = $"PIC {pic.Name} already has many tickets";
                    picId = null;
                }
            }
        }

        await _tickets.AssignAsync(ticketId, categoryId, picId, reason);
        return (categoryId, picId, reason);
    }

    private async Task<string> SummarizeAsync(string raw)
    {
        var prompt = new StringBuilder()
            .AppendLine("Convert the following ticket assignment into a JSON object with 'categoryId' and 'picId' fields.")
            .AppendLine("If not possible, return an empty object.")
            .AppendLine(raw)
            .ToString();
        var json = (await _llm.GenerateAsync(prompt)).Trim();

        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('\n');
            var end = json.LastIndexOf("```");
            if (start >= 0 && end > start)
                json = json.Substring(start + 1, end - start - 1).Trim();
            else
                json = json.Trim('`');
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("LLM summary was not a JSON object.");
        return JsonSerializer.Serialize(doc.RootElement);
    }
}

