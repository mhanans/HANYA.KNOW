using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Services;

public class GitHubTokenStore
{
    private readonly string _connectionString;
    private readonly ILogger<GitHubTokenStore> _logger;

    public GitHubTokenStore(IOptions<PostgresOptions> dbOptions, ILogger<GitHubTokenStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<GitHubToken?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT access_token, token_type, scope FROM user_github_tokens WHERE user_id=@u";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new GitHubToken
        {
            AccessToken = reader.GetString(0),
            TokenType = reader.GetString(1),
            Scope = reader.GetString(2)
        };
    }

    public async Task SaveAsync(int userId, GitHubToken token, CancellationToken cancellationToken = default)
    {
        const string sql = @"INSERT INTO user_github_tokens(user_id, access_token, token_type, scope, updated_at)
                               VALUES (@u, @a, @t, @s, NOW())
                               ON CONFLICT (user_id) DO UPDATE
                               SET access_token=@a, token_type=@t, scope=@s, updated_at=NOW()";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("a", token.AccessToken);
        cmd.Parameters.AddWithValue("t", token.TokenType);
        cmd.Parameters.AddWithValue("s", token.Scope);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Stored GitHub token for user {UserId}", userId);
    }

    public async Task DeleteAsync(int userId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM user_github_tokens WHERE user_id=@u";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("u", userId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Removed GitHub token for user {UserId}", userId);
    }
}

public class GitHubToken
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
