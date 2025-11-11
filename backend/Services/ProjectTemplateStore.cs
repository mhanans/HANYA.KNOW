using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

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
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, payload);
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
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, payload);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            throw new KeyNotFoundException($"Template {id} not found");
        }
    }

    public async Task<ProjectTemplate> DuplicateAsync(int id, int? userId)
    {
        var source = await GetAsync(id);
        if (source is null)
        {
            throw new KeyNotFoundException($"Template {id} not found");
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var newName = await GenerateCopyNameAsync(source.TemplateName, conn);
        source.Id = null;
        source.TemplateName = newName;

        const string sql = @"INSERT INTO project_templates (template_name, template_data, created_by_user_id, created_at, last_modified_at)
                             VALUES (@name, @data, @user, NOW(), NOW()) RETURNING id";
        var payload = JsonSerializer.Serialize(source, JsonOptions);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", newName);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, payload);
        cmd.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        var newId = Convert.ToInt32(result);
        source.Id = newId;
        return source;
    }

    private static async Task<string> GenerateCopyNameAsync(string originalName, NpgsqlConnection conn)
    {
        var baseName = string.IsNullOrWhiteSpace(originalName)
            ? "Template (Copy)"
            : $"{originalName} (Copy)";

        var candidate = baseName;
        var counter = 2;
        while (await NameExistsAsync(candidate, conn))
        {
            candidate = $"{baseName} {counter}";
            counter++;
        }

        return candidate;
    }

    private static async Task<bool> NameExistsAsync(string name, NpgsqlConnection conn)
    {
        const string sql = "SELECT 1 FROM project_templates WHERE template_name = @name LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value;
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

    public async Task<IReadOnlyList<string>> ListEstimationColumnsAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT template_data -> 'estimationColumns' FROM project_templates";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var json = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                        {
                            var value = element.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                set.Add(value.Trim());
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse estimation columns from template data record.");
            }
        }

        return set
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<TemplateSectionItemReference>> ListTemplateTaskReferencesAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT template_data -> 'sections' FROM project_templates";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var sectionOrderLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemLookup = new Dictionary<string, TemplateSectionItemReference>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var json = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var sectionIndex = 0;
                foreach (var sectionElement in doc.RootElement.EnumerateArray())
                {
                    if (sectionElement.ValueKind != JsonValueKind.Object)
                    {
                        sectionIndex++;
                        continue;
                    }

                    if (!sectionElement.TryGetProperty("sectionName", out var sectionNameElement) || sectionNameElement.ValueKind != JsonValueKind.String)
                    {
                        sectionIndex++;
                        continue;
                    }

                    var sectionName = sectionNameElement.GetString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sectionName))
                    {
                        sectionIndex++;
                        continue;
                    }

                    sectionNames.Add(sectionName);
                    if (!sectionOrderLookup.TryGetValue(sectionName, out var existingSectionOrder) || sectionIndex < existingSectionOrder)
                    {
                        sectionOrderLookup[sectionName] = sectionIndex;
                    }

                    var itemsElement = sectionElement.TryGetProperty("items", out var rawItems) && rawItems.ValueKind == JsonValueKind.Array
                        ? rawItems
                        : default;

                    var hasItems = itemsElement.ValueKind == JsonValueKind.Array && itemsElement.GetArrayLength() > 0;
                    if (hasItems)
                    {
                        var itemIndex = 0;
                        foreach (var itemElement in itemsElement.EnumerateArray())
                        {
                            if (itemElement.ValueKind != JsonValueKind.Object)
                            {
                                itemIndex++;
                                continue;
                            }

                            if (!itemElement.TryGetProperty("itemName", out var itemNameElement) || itemNameElement.ValueKind != JsonValueKind.String)
                            {
                                itemIndex++;
                                continue;
                            }

                            var itemName = itemNameElement.GetString()?.Trim() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(itemName))
                            {
                                itemIndex++;
                                continue;
                            }

                            var key = $"{sectionName}\0{itemName}";
                            if (itemLookup.TryGetValue(key, out var existing))
                            {
                                if (sectionIndex < existing.SectionOrder)
                                {
                                    existing.SectionOrder = sectionIndex;
                                }

                                if (itemIndex < existing.ItemOrder)
                                {
                                    existing.ItemOrder = itemIndex;
                                }

                                itemLookup[key] = existing;
                            }
                            else
                            {
                                itemLookup[key] = new TemplateSectionItemReference
                                {
                                    SectionName = sectionName,
                                    SectionOrder = sectionIndex,
                                    ItemName = itemName,
                                    ItemOrder = itemIndex
                                };
                            }

                            itemIndex++;
                        }
                    }

                    sectionIndex++;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse section items from template data record.");
            }
        }

        foreach (var sectionName in sectionNames)
        {
            var key = $"{sectionName}\0";
            if (!itemLookup.ContainsKey(key))
            {
                var order = sectionOrderLookup.TryGetValue(sectionName, out var sectionOrder)
                    ? sectionOrder
                    : int.MaxValue;
                itemLookup[key] = new TemplateSectionItemReference
                {
                    SectionName = sectionName,
                    SectionOrder = order,
                    ItemName = string.Empty,
                    ItemOrder = -1
                };
            }
            else if (sectionOrderLookup.TryGetValue(sectionName, out var normalizedOrder))
            {
                var reference = itemLookup[key];
                if (normalizedOrder < reference.SectionOrder)
                {
                    reference.SectionOrder = normalizedOrder;
                    itemLookup[key] = reference;
                }
            }
        }

        foreach (var reference in itemLookup.Values)
        {
            if (sectionOrderLookup.TryGetValue(reference.SectionName, out var normalizedOrder))
            {
                reference.SectionOrder = Math.Min(reference.SectionOrder, normalizedOrder);
            }
        }

        return itemLookup.Values
            .OrderBy(reference => reference.SectionOrder)
            .ThenBy(reference => reference.ItemOrder)
            .ThenBy(reference => reference.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
