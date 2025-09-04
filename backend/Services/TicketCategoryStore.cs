using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace backend.Services;

public class TicketCategoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<TicketCategoryStore> _logger;

    public TicketCategoryStore(IOptions<PostgresOptions> dbOptions, ILogger<TicketCategoryStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<TicketCategory>> ListAsync()
    {
        const string sql = "SELECT id, ticket_type, description, sample_json FROM ticket_categories ORDER BY ticket_type";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TicketCategory>();
        while (await reader.ReadAsync())
        {
            list.Add(new TicketCategory
            {
                Id = reader.GetInt32(0),
                TicketType = reader.GetString(1),
                Description = reader.GetString(2),
                SampleJson = reader.GetString(3)
            });
        }
        return list;
    }

    public async Task<int> CreateAsync(string ticketType, string description, string sampleJson)
    {
        const string sql = "INSERT INTO ticket_categories(ticket_type, description, sample_json) VALUES (@t,@d,@j) RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("t", ticketType);
        cmd.Parameters.AddWithValue("d", description);
        cmd.Parameters.AddWithValue("j", sampleJson);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(int id, string ticketType, string description, string sampleJson)
    {
        const string sql = "UPDATE ticket_categories SET ticket_type=@t, description=@d, sample_json=@j WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("t", ticketType);
        cmd.Parameters.AddWithValue("d", description);
        cmd.Parameters.AddWithValue("j", sampleJson);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM ticket_categories WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        try
        {
            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                throw new KeyNotFoundException();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            _logger.LogWarning(ex, "Ticket category {Id} is in use", id);
            throw new InvalidOperationException("Ticket category is in use");
        }
    }
}

public class TicketCategory
{
    public int Id { get; set; }
    public string TicketType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SampleJson { get; set; } = string.Empty;
}
