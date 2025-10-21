using System;
using System.Text.Json;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace backend.Services;

public class ProjectTemplateStore
{
    private readonly string _connectionString;
    private readonly ILogger<ProjectTemplateStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProjectTemplateStore(IOptions<PostgresOptions> dbOptions, ILogger<ProjectTemplateStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<ProjectTemplateMetadata>> ListMetadataAsync()
    {
        const string sql = @"SELECT t.id, t.template_name, COALESCE(u.username, 'system') AS created_by, COALESCE(t.last_modified_at, t.created_at) AS last_modified
                               FROM project_templates t
                               LEFT JOIN users u ON u.id = t.created_by_user_id
                               ORDER BY COALESCE(t.last_modified_at, t.created_at) DESC, t.template_name";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ProjectTemplateMetadata>();
        while (await reader.ReadAsync())
        {
            list.Add(new ProjectTemplateMetadata
            {
                Id = reader.GetInt32(0),
                TemplateName = reader.GetString(1),
                CreatedBy = reader.GetString(2),
                LastModified = reader.IsDBNull(3) ? DateTime.UtcNow : reader.GetDateTime(3)
            });
        }
        return list;
    }

    public async Task<ProjectTemplate?> GetAsync(int id)
    {
        const string sql = "SELECT template_data FROM project_templates WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull) return null;
        try
        {
            var template = JsonSerializer.Deserialize<ProjectTemplate>(result.ToString() ?? string.Empty, JsonOptions);
            if (template != null)
            {
                template.Id = id;
            }
            return template;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize template {TemplateId}", id);
            throw;
        }
    }

    public async Task<int> CreateAsync(ProjectTemplate template, int? userId)
    {
        const string sql = @"INSERT INTO project_templates (template_name, template_data, created_by_user_id, created_at, last_modified_at)
                             VALUES (@name, @data, @user, NOW(), NOW()) RETURNING id";
        var payload = JsonSerializer.Serialize(template, JsonOptions);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", template.TemplateName);
        cmd.Parameters.AddWithValue("data", payload);
        cmd.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task UpdateAsync(int id, ProjectTemplate template)
    {
        const string sql = @"UPDATE project_templates
                             SET template_name=@name, template_data=@data, last_modified_at=NOW()
                             WHERE id=@id";
        var payload = JsonSerializer.Serialize(template, JsonOptions);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", template.TemplateName);
        cmd.Parameters.AddWithValue("data", payload);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            throw new KeyNotFoundException($"Template {id} not found");
        }
    }

    public async Task DeleteAsync(int id)
    {
        const string sql = "DELETE FROM project_templates WHERE id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            throw new KeyNotFoundException($"Template {id} not found");
        }
    }
}
