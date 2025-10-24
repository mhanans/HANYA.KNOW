using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class CostEstimationStore
{
    private readonly string _connectionString;
    private readonly ILogger<CostEstimationStore> _logger;

    public CostEstimationStore(IOptions<PostgresOptions> options, ILogger<CostEstimationStore> logger)
    {
        _connectionString = options.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task SaveAsync(CostEstimationResult result, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT INTO cost_estimations (assessment_id, project_name, template_name, generated_at, result_json)
                             VALUES (@id, @project, @template, NOW(), @payload)
                             ON CONFLICT (assessment_id) DO UPDATE
                             SET project_name = EXCLUDED.project_name,
                                 template_name = EXCLUDED.template_name,
                                 generated_at = NOW(),
                                 result_json = EXCLUDED.result_json";

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", result.AssessmentId);
        cmd.Parameters.AddWithValue("project", result.ProjectName ?? string.Empty);
        cmd.Parameters.AddWithValue("template", result.TemplateName ?? string.Empty);
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, json);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CostEstimationResult?> GetAsync(int assessmentId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT result_json FROM cost_estimations WHERE assessment_id = @id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (reader.IsDBNull(0))
        {
            return null;
        }

        var json = reader.GetString(0);
        try
        {
            return JsonSerializer.Deserialize<CostEstimationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize cost estimation stored for assessment {AssessmentId}.", assessmentId);
            return null;
        }
    }
}
