using Npgsql;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace backend.Services;

public class TicketStore
{
    private readonly string _connectionString;

    public TicketStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    public async Task<List<Ticket>> ListAsync()
    {
        const string sql = "SELECT id, ticket_number, complaint, detail, category_id, pic_id, reason, created_at FROM tickets ORDER BY created_at DESC";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Ticket>();
        while (await reader.ReadAsync())
        {
            list.Add(new Ticket
            {
                Id = reader.GetInt32(0),
                TicketNumber = reader.GetString(1),
                Complaint = reader.GetString(2),
                Detail = reader.GetString(3),
                CategoryId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PicId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }
        return list;
    }

    public async Task<Ticket?> GetAsync(int id)
    {
        const string sql = "SELECT id, ticket_number, complaint, detail, category_id, pic_id, reason, created_at FROM tickets WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Ticket
            {
                Id = reader.GetInt32(0),
                TicketNumber = reader.GetString(1),
                Complaint = reader.GetString(2),
                Detail = reader.GetString(3),
                CategoryId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PicId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            };
        }
        return null;
    }

    public async Task<int> CreateAsync(string ticketNumber, string complaint, string detail)
    {
        const string sql = "INSERT INTO tickets(ticket_number, complaint, detail) VALUES (@n,@c,@d) RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("n", ticketNumber);
        cmd.Parameters.AddWithValue("c", complaint);
        cmd.Parameters.AddWithValue("d", detail);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return id;
    }

    public async Task AssignAsync(int id, int? categoryId, int? picId, string? reason)
    {
        const string sql = "UPDATE tickets SET category_id=@c, pic_id=@p, reason=@r WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("c", categoryId.HasValue ? (object)categoryId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("p", picId.HasValue ? (object)picId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("r", reason != null ? (object)reason : DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }
}

public class Ticket
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Complaint { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? PicId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
