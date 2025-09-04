using Npgsql;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class TicketAssignmentResultStore
{
    private readonly string _connectionString;

    public TicketAssignmentResultStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    public async Task AddAsync(int ticketId, string response, string responseJson)
    {
        const string sql = "INSERT INTO ticket_ai_assignments(ticket_id, response, response_json) VALUES (@t,@r,@j)";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", ticketId);
        cmd.Parameters.AddWithValue("r", response);
        cmd.Parameters.AddWithValue("j", responseJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<TicketAssignmentResult?> GetLatestAsync(int ticketId)
    {
        const string sql = "SELECT id, ticket_id, response, response_json, created_at FROM ticket_ai_assignments WHERE ticket_id=@t ORDER BY created_at DESC LIMIT 1";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", ticketId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new TicketAssignmentResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4));
        }
        return null;
    }

    public async Task UpdateJsonAsync(int id, string responseJson)
    {
        const string sql = "UPDATE ticket_ai_assignments SET response_json=@j WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("j", responseJson);
        await cmd.ExecuteNonQueryAsync();
    }
}

public record TicketAssignmentResult(int Id, int TicketId, string Response, string ResponseJson, DateTime CreatedAt);
