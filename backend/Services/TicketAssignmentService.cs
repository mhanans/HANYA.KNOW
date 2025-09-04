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

    public async Task<(int? categoryId, int? picId)> AutoAssignAsync(Ticket ticket)
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
Return your answer in JSON with keys \"categoryId\" and \"picId\".
Ticket:
Number: {ticket.TicketNumber}
Complaint: {ticket.Complaint}
Detail: {ticket.Detail}";

        try
        {
            var response = await _llm.GenerateAsync(prompt);
            using var doc = JsonDocument.Parse(response);
            var categoryId = doc.RootElement.GetProperty("categoryId").GetInt32();
            var picId = doc.RootElement.GetProperty("picId").GetInt32();
            await _tickets.AssignAsync(ticket.Id, categoryId, picId);
            return (categoryId, picId);
        }
        catch
        {
            return (null, null);
        }
    }
}
