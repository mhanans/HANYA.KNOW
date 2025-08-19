using Npgsql;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class RecommendationStore
{
    private readonly string _connectionString;

    public RecommendationStore(IOptions<PostgresOptions> dbOptions)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
    }

    public async Task<CvRecommendation> AddAsync(string position, string details, string summary)
    {
        const string sql = "INSERT INTO cv_recommendations(position, details, summary) VALUES (@p,@d,@s) RETURNING id, created_at";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", position);
        cmd.Parameters.AddWithValue("d", details);
        cmd.Parameters.AddWithValue("s", summary);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var id = reader.GetInt32(0);
        var created = reader.GetDateTime(1);
        return new CvRecommendation(id, position, details, summary, created);
    }

    public async Task<IReadOnlyList<CvRecommendation>> ListAsync()
    {
        const string sql = "SELECT id, position, details, summary, created_at FROM cv_recommendations ORDER BY created_at DESC";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<CvRecommendation>();
        while (await reader.ReadAsync())
        {
            list.Add(new CvRecommendation(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4)));
        }
        return list;
    }

    public async Task<CvRecommendation?> GetAsync(int id)
    {
        const string sql = "SELECT id, position, details, summary, created_at FROM cv_recommendations WHERE id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new CvRecommendation(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4));
        }
        return null;
    }

    public async Task<CvRecommendation> UpdateSummaryAsync(int id, string summary)
    {
        const string sql = "UPDATE cv_recommendations SET summary=@s, created_at=NOW() WHERE id=@id RETURNING position, details, created_at";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", summary);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var position = reader.GetString(0);
        var details = reader.GetString(1);
        var created = reader.GetDateTime(2);
        return new CvRecommendation(id, position, details, summary, created);
    }
}

public record CvRecommendation(int Id, string Position, string Details, string Summary, DateTime CreatedAt);
