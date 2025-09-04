using Npgsql;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Collections.Generic;

namespace backend.Services;

public class PicStore
{
    private readonly string _connectionString;

    public PicStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    public async Task<List<Pic>> ListAsync()
    {
        const string sql = @"SELECT p.id, p.name, p.availability,
       COALESCE(c.categories, '{}') AS categories,
       COALESCE(t.ticket_count, 0) AS ticket_count
FROM pics p
LEFT JOIN (
    SELECT pic_id, array_agg(DISTINCT ticket_category_id) AS categories
    FROM pic_ticket_categories
    GROUP BY pic_id
) c ON p.id = c.pic_id
LEFT JOIN (
    SELECT pic_id, COUNT(*) AS ticket_count
    FROM tickets
    GROUP BY pic_id
) t ON p.id = t.pic_id
ORDER BY p.name";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Pic>();
        while (await reader.ReadAsync())
        {
            var categories = reader.IsDBNull(3) ? new List<int>() : reader.GetFieldValue<int[]>(3).ToList();
            list.Add(new Pic
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Availability = reader.GetBoolean(2),
                CategoryIds = categories,
                TicketCount = reader.GetInt32(4)
            });
        }
        return list;
    }

    public async Task<int> CreateAsync(string name, bool availability, IEnumerable<int> categoryIds)
    {
        const string sql = "INSERT INTO pics(name, availability) VALUES (@n,@a) RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("n", name);
        cmd.Parameters.AddWithValue("a", availability);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await UpsertCategoriesAsync(conn, id, categoryIds);
        return id;
    }

    public async Task UpdateAsync(int id, string name, bool availability, IEnumerable<int> categoryIds)
    {
        const string sql = "UPDATE pics SET name=@n, availability=@a WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("n", name);
            cmd.Parameters.AddWithValue("a", availability);
            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                throw new KeyNotFoundException();
        }
        await using (var del = new NpgsqlCommand("DELETE FROM pic_ticket_categories WHERE pic_id=@id", conn))
        {
            del.Parameters.AddWithValue("id", id);
            await del.ExecuteNonQueryAsync();
        }
        await UpsertCategoriesAsync(conn, id, categoryIds);
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM pics WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }

    private static async Task UpsertCategoriesAsync(NpgsqlConnection conn, int picId, IEnumerable<int> categoryIds)
    {
        foreach (var cid in categoryIds)
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO pic_ticket_categories(pic_id, ticket_category_id) VALUES (@p,@c)", conn);
            cmd.Parameters.AddWithValue("p", picId);
            cmd.Parameters.AddWithValue("c", cid);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public class Pic
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Availability { get; set; }
    public List<int> CategoryIds { get; set; } = new();
    public int TicketCount { get; set; }
}
