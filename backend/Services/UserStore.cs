using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace backend.Services;

public class UserStore
{
    private readonly string _connectionString;
    private readonly ILogger<UserStore> _logger;

    public UserStore(IOptions<PostgresOptions> dbOptions, ILogger<UserStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<User>> ListAsync()
    {
        const string sql = @"SELECT u.id, u.username,
            ARRAY(SELECT role_id FROM user_roles WHERE user_id = u.id) AS role_ids
            FROM users u ORDER BY u.username";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<User>();
        while (await reader.ReadAsync())
        {
            var roles = reader.IsDBNull(2)
                ? new List<int>()
                : reader.GetFieldValue<int[]>(2).ToList();
            list.Add(new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                RoleIds = roles
            });
        }
        return list;
    }

    public async Task<int> CreateAsync(User user)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql = "INSERT INTO users(username, password) VALUES (@u, @p) RETURNING id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("u", user.Username);
        var hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
        cmd.Parameters.AddWithValue("p", hashed);
        var result = await cmd.ExecuteScalarAsync();
        var id = Convert.ToInt32(result);

        foreach (var rid in user.RoleIds.Distinct())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO user_roles(user_id, role_id) VALUES (@uid, @rid)", conn, tx);
            insert.Parameters.AddWithValue("uid", id);
            insert.Parameters.AddWithValue("rid", rid);
            await insert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return id;
    }

    public async Task UpdateAsync(User user)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql = "UPDATE users SET username=@u, password=@p WHERE id=@id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("u", user.Username);
        var hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
        cmd.Parameters.AddWithValue("p", hashed);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();

        await using (var del = new NpgsqlCommand("DELETE FROM user_roles WHERE user_id=@id", conn, tx))
        {
            del.Parameters.AddWithValue("id", user.Id);
            await del.ExecuteNonQueryAsync();
        }

        foreach (var rid in user.RoleIds.Distinct())
        {
            await using var insert = new NpgsqlCommand("INSERT INTO user_roles(user_id, role_id) VALUES (@uid, @rid)", conn, tx);
            insert.Parameters.AddWithValue("uid", user.Id);
            insert.Parameters.AddWithValue("rid", rid);
            await insert.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM users WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new KeyNotFoundException();
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        const string sql = @"SELECT u.id, u.password,
            ARRAY(SELECT role_id FROM user_roles WHERE user_id = u.id) AS role_ids
            FROM users u WHERE u.username=@u";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var dbPass = reader.GetString(1);
            if (!BCrypt.Net.BCrypt.Verify(password, dbPass)) return null;
            var roles = reader.IsDBNull(2)
                ? new List<int>()
                : reader.GetFieldValue<int[]>(2).ToList();
            return new User
            {
                Id = reader.GetInt32(0),
                Username = username,
                Password = string.Empty,
                RoleIds = roles
            };
        }
        return null;
    }

    public async Task<User?> FindByUsernameAsync(string username)
    {
        const string sql = @"SELECT u.id,
            ARRAY(SELECT role_id FROM user_roles WHERE user_id = u.id) AS role_ids
            FROM users u WHERE u.username=@u";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var roles = reader.IsDBNull(1)
                ? new List<int>()
                : reader.GetFieldValue<int[]>(1).ToList();
            return new User
            {
                Id = reader.GetInt32(0),
                Username = username,
                RoleIds = roles
            };
        }

        return null;
    }
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<int> RoleIds { get; set; } = new();
}
