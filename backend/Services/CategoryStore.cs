using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class CategoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<CategoryStore> _logger;

    public CategoryStore(IOptions<PostgresOptions> dbOptions, ILogger<CategoryStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<Category>> ListAsync()
    {
        const string sql = "SELECT id, name FROM categories ORDER BY name";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Category>();
        while (await reader.ReadAsync())
            list.Add(new Category { Id = reader.GetInt32(0), Name = reader.GetString(1) });
        return list;
    }

    public async Task<int> CreateAsync(string name)
    {
        const string sql = "INSERT INTO categories(name) VALUES (@name) RETURNING id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(int id, string name)
    {
        const string sql = "UPDATE categories SET name=@name WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM categories WHERE id=@id";
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
            _logger.LogWarning(ex, "Category {Id} is in use", id);
            throw new InvalidOperationException("Category is in use");
        }
    }
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
