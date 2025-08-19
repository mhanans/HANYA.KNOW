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

    public async Task<CvRecommendation> AddAsync(string position, string details, string summary, string summaryJson)
    {
        const string sql = "INSERT INTO cv_recommendations(position, details, summary, summary_json) VALUES (@p,@d,@s,@j) RETURNING id, created_at";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", position);
        cmd.Parameters.AddWithValue("d", details);
        cmd.Parameters.AddWithValue("s", summary);
        cmd.Parameters.AddWithValue("j", summaryJson);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var id = reader.GetInt32(0);
        var created = reader.GetDateTime(1);
        return new CvRecommendation(id, position, details, summary, summaryJson, created);
    }

    public async Task<IReadOnlyList<CvRecommendation>> ListAsync()
    {
        const string sql = "SELECT id, position, details, summary, summary_json, created_at FROM cv_recommendations ORDER BY created_at DESC";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<CvRecommendation>();
        while (await reader.ReadAsync())
        {
            list.Add(new CvRecommendation(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDateTime(5)));
        }
        return list;
    }

    public async Task<CvRecommendation?> GetAsync(int id)
    {
        const string sql = "SELECT id, position, details, summary, summary_json, created_at FROM cv_recommendations WHERE id = @id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new CvRecommendation(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDateTime(5));
        }
        return null;
    }

    public async Task<CvRecommendation> UpdateAsync(int id, string summary, string summaryJson)
    {
        const string sql = "UPDATE cv_recommendations SET summary=@s, summary_json=@j, created_at=NOW() WHERE id=@id RETURNING position, details, created_at";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("s", summary);
        cmd.Parameters.AddWithValue("j", summaryJson);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var position = reader.GetString(0);
        var details = reader.GetString(1);
        var created = reader.GetDateTime(2);
        return new CvRecommendation(id, position, details, summary, summaryJson, created);
    }

    public async Task<CvRecommendation> UpdateSummaryJsonAsync(int id, string summaryJson)
    {
        const string sql = "UPDATE cv_recommendations SET summary_json=@j, created_at=NOW() WHERE id=@id RETURNING position, details, summary, created_at";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("j", summaryJson);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var position = reader.GetString(0);
        var details = reader.GetString(1);
        var summary = reader.GetString(2);
        var created = reader.GetDateTime(3);
        return new CvRecommendation(id, position, details, summary, summaryJson, created);
    }
}

public record CvRecommendation(int Id, string Position, string Details, string Summary, string SummaryJson, DateTime CreatedAt);
