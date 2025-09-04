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

    public TicketAssignmentService(
        TicketCategoryStore categories,
        PicStore pics,
        TicketStore tickets,
        LlmClient llm)
    {
        _categories = categories;
        _pics = pics;
        _tickets = tickets;
        _llm = llm;
    }

    public async Task<(int? categoryId, int? picId, string? reason)> AutoAssignAsync(Ticket ticket)
    {
        var categories = await _categories.ListAsync();
        var pics = await _pics.ListAsync();

        var catLines = string.Join("\n", categories.Select(c => $"{c.Id}: {c.TicketType} - {c.Description} (sample: {c.SampleJson})"));
        var picLines = string.Join("\n", pics.Select(p => $"{p.Id}: {p.Name} handles [{string.Join(",", p.CategoryIds)}] and is {(p.Availability ? "available" : "unavailable")}"));

        var prompt = $@"You are a support ticket router. Choose the best matching ticket category and an available PIC.
Ticket categories:
{catLines}
PICs:
{picLines}
Return your answer in JSON with keys ""categoryId"" and ""picId"" (use null if unknown).
Ticket:
Number: {ticket.TicketNumber}
Complaint: {ticket.Complaint}
Detail: {ticket.Detail}";

        int? categoryId = null;
        int? picId = null;
        string? reason = null;

        try
        {
            var response = await _llm.GenerateAsync(prompt);
            using var doc = JsonDocument.Parse(response);

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
}

