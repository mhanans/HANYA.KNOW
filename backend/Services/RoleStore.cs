using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class RoleStore
{
    private readonly string _connectionString;
    private readonly ILogger<RoleStore> _logger;

    public RoleStore(IOptions<PostgresOptions> dbOptions, ILogger<RoleStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<Role>> ListAsync()
    {
        const string sql = @"SELECT r.id, r.name, r.all_categories,
            ARRAY(SELECT category_id FROM role_categories WHERE role_id=r.id) AS cat_ids,
            ARRAY(SELECT ui_id FROM role_ui WHERE role_id=r.id) AS ui_ids
            FROM roles r ORDER BY r.name";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Role>();
        while (await reader.ReadAsync())
        {
            var ids = reader.GetFieldValue<int[]>(3).ToList();
            var uis = reader.GetFieldValue<int[]>(4).ToList();
            list.Add(new Role
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                AllCategories = reader.GetBoolean(2),
                CategoryIds = ids,
                UiIds = uis
            });
        }
        return list;
    }

    public async Task<int> CreateAsync(Role role)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql = "INSERT INTO roles(name, all_categories) VALUES (@name, @all) RETURNING id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", role.Name);
        cmd.Parameters.AddWithValue("all", role.AllCategories);
        var result = await cmd.ExecuteScalarAsync();
        var id = Convert.ToInt32(result);

        if (!role.AllCategories && role.CategoryIds.Count > 0)
        {
            foreach (var cid in role.CategoryIds.Distinct())
            {
                await using var insert = new NpgsqlCommand("INSERT INTO role_categories(role_id, category_id) VALUES (@rid, @cid)", conn, tx);
                insert.Parameters.AddWithValue("rid", id);
                insert.Parameters.AddWithValue("cid", cid);
                await insert.ExecuteNonQueryAsync();
            }
        }

        foreach (var ui in role.UiIds.Distinct())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO role_ui(role_id, ui_id) VALUES (@rid, @ui)", conn, tx);
            insert.Parameters.AddWithValue("rid", id);
            insert.Parameters.AddWithValue("ui", ui);
            await insert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return id;
    }

    public async Task UpdateAsync(Role role)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql = "UPDATE roles SET name=@name, all_categories=@all WHERE id=@id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", role.Id);
        cmd.Parameters.AddWithValue("name", role.Name);
        cmd.Parameters.AddWithValue("all", role.AllCategories);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            throw new KeyNotFoundException();
        }

        await using (var del = new NpgsqlCommand("DELETE FROM role_categories WHERE role_id=@id", conn, tx))
        {
            del.Parameters.AddWithValue("id", role.Id);
            await del.ExecuteNonQueryAsync();
        }
        await using (var delUi = new NpgsqlCommand("DELETE FROM role_ui WHERE role_id=@id", conn, tx))
        {
            delUi.Parameters.AddWithValue("id", role.Id);
            await delUi.ExecuteNonQueryAsync();
        }

        if (!role.AllCategories && role.CategoryIds.Count > 0)
        {
            foreach (var cid in role.CategoryIds.Distinct())
            {
                await using var insert = new NpgsqlCommand("INSERT INTO role_categories(role_id, category_id) VALUES (@rid, @cid)", conn, tx);
                insert.Parameters.AddWithValue("rid", role.Id);
                insert.Parameters.AddWithValue("cid", cid);
                await insert.ExecuteNonQueryAsync();
            }
        }
        foreach (var ui in role.UiIds.Distinct())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO role_ui(role_id, ui_id) VALUES (@rid, @ui)", conn, tx);
            insert.Parameters.AddWithValue("rid", role.Id);
            insert.Parameters.AddWithValue("ui", ui);
            await insert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM roles WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool AllCategories { get; set; }
    public List<int> CategoryIds { get; set; } = new();
    public List<int> UiIds { get; set; } = new();
}
