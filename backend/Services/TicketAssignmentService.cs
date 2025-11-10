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

        var prompt = $@"You are a support ticket router. Answer only in Indonesian language. Choose the best matching ticket category and an available PIC. Prefer PICs with fewer active tickets unless only one PIC handles the category. Explain briefly why the category fits.
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
        var reasons = new List<string>();

        string raw;
        string summaryJson = string.Empty;
        try
        {
            raw = await _llm.GenerateAsync(prompt, AiProcesses.TicketAutoAssignment);
            try { summaryJson = await SummarizeAsync(raw); } catch { }
            await _results.AddAsync(ticket.Id, raw, summaryJson);

            using var doc = JsonDocument.Parse(summaryJson);

            if (doc.RootElement.TryGetProperty("categoryId", out var catElem) && catElem.ValueKind == JsonValueKind.Number)
                categoryId = catElem.GetInt32();

            if (doc.RootElement.TryGetProperty("picId", out var picElem) && picElem.ValueKind == JsonValueKind.Number)
                picId = picElem.GetInt32();

            if (doc.RootElement.TryGetProperty("reason", out var reasonElem) && reasonElem.ValueKind == JsonValueKind.String)
                reasons.Add(reasonElem.GetString()!);

            if (categoryId == null || !categories.Any(c => c.Id == categoryId))
            {
                reasons.Add("AI returned unknown category");
                categoryId = null;
                picId = null;
            }
            else if (picId == null)
            {
                reasons.Add("AI did not select a PIC");
                picId = null;
            }
            else
            {
                var pic = pics.FirstOrDefault(p => p.Id == picId);
                if (pic == null)
                {
                    reasons.Add("AI returned unknown PIC");
                    picId = null;
                }
                else if (!pic.Availability)
                {
                    reasons.Add($"PIC {pic.Name} unavailable");
                    picId = null;
                }
                else if (!pic.CategoryIds.Contains(categoryId.Value))
                {
                    reasons.Add($"PIC {pic.Name} cannot handle category {categoryId}");
                    picId = null;
                }
                else
                {
                    var same = pics.Where(p => p.Availability && p.CategoryIds.Contains(categoryId.Value)).ToList();
                    if (same.Count > 1 && pic.TicketCount > same.Min(p => p.TicketCount))
                    {
                        reasons.Add($"PIC {pic.Name} already has many tickets");
                        picId = null;
                    }
                }
            }
        }
        catch
        {
            reasons.Add("AI assignment failed");
            categoryId = null;
            picId = null;
        }

        var reason = reasons.Count > 0 ? string.Join("; ", reasons) : null;
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
        var reasons = new List<string>();

        using var doc = JsonDocument.Parse(summaryJson);

        if (doc.RootElement.TryGetProperty("categoryId", out var catElem) && catElem.ValueKind == JsonValueKind.Number)
            categoryId = catElem.GetInt32();

        if (doc.RootElement.TryGetProperty("picId", out var picElem) && picElem.ValueKind == JsonValueKind.Number)
            picId = picElem.GetInt32();

        if (doc.RootElement.TryGetProperty("reason", out var reasonElem) && reasonElem.ValueKind == JsonValueKind.String)
            reasons.Add(reasonElem.GetString()!);

        if (categoryId == null || !categories.Any(c => c.Id == categoryId))
        {
            reasons.Add("AI returned unknown category");
            categoryId = null;
            picId = null;
        }
        else if (picId == null)
        {
            reasons.Add("AI did not select a PIC");
            picId = null;
        }
        else
        {
            var pic = pics.FirstOrDefault(p => p.Id == picId);
            if (pic == null)
            {
                reasons.Add("AI returned unknown PIC");
                picId = null;
            }
            else if (!pic.Availability)
            {
                reasons.Add($"PIC {pic.Name} unavailable");
                picId = null;
            }
            else if (!pic.CategoryIds.Contains(categoryId.Value))
            {
                reasons.Add($"PIC {pic.Name} cannot handle category {categoryId}");
                picId = null;
            }
            else
            {
                var same = pics.Where(p => p.Availability && p.CategoryIds.Contains(categoryId.Value)).ToList();
                if (same.Count > 1 && pic.TicketCount > same.Min(p => p.TicketCount))
                {
                    reasons.Add($"PIC {pic.Name} already has many tickets");
                    picId = null;
                }
            }
        }

        var reason = reasons.Count > 0 ? string.Join("; ", reasons) : null;
        await _tickets.AssignAsync(ticketId, categoryId, picId, reason);
        return (categoryId, picId, reason);
    }

    private async Task<string> SummarizeAsync(string raw)
    {
        var prompt = new StringBuilder()
            .AppendLine("Convert the following ticket assignment into a JSON object with 'categoryId', 'picId', and 'reason' (Answer only in Indonesian language. brief explanation for the category).")
            .AppendLine("If not possible, return an empty object.")
            .AppendLine(raw)
            .ToString();
        var json = (await _llm.GenerateAsync(prompt, AiProcesses.TicketSummary)).Trim();

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

