using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace backend.Services;

public class PrototypeStore
{
    private readonly string _connectionString;
    private readonly ILogger<PrototypeStore> _logger;

    public PrototypeStore(IOptions<PostgresOptions> dbOptions, ILogger<PrototypeStore> logger)
    {
        _connectionString = dbOptions.Value.Postgres ?? string.Empty;
        _logger = logger;
    }

    public async Task SaveAsync(PrototypeRecord record, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await EnsureSchemaAsync(conn, cancellationToken);

        const string sql = @"INSERT INTO assessment_prototypes (assessment_id, project_name, generated_at, storage_path, status)
                             VALUES (@id, @project, @generated, @path, @status)
                             ON CONFLICT (assessment_id)
                             DO UPDATE SET project_name = EXCLUDED.project_name,
                                           generated_at = EXCLUDED.generated_at,
                                           storage_path = EXCLUDED.storage_path,
                                           status = EXCLUDED.status";
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", record.AssessmentId);
        cmd.Parameters.AddWithValue("project", record.ProjectName);
        cmd.Parameters.AddWithValue("generated", record.GeneratedAt);
        cmd.Parameters.AddWithValue("path", record.StoragePath);
        cmd.Parameters.AddWithValue("status", record.Status);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        // Simple check to ensure table exists if schema.sql wasn't run
        const string sql = @"
            CREATE TABLE IF NOT EXISTS assessment_prototypes (
                assessment_id INT PRIMARY KEY REFERENCES project_assessments(id) ON DELETE CASCADE,
                project_name TEXT NOT NULL,
                generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                storage_path TEXT NOT NULL
            );
            ALTER TABLE assessment_prototypes ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'Completed';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PrototypeRecord?> GetAsync(int assessmentId, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT assessment_id, project_name, generated_at, storage_path, status FROM assessment_prototypes WHERE assessment_id=@id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PrototypeRecord
        {
            AssessmentId = reader.GetInt32(0),
            ProjectName = reader.GetString(1),
            GeneratedAt = reader.GetDateTime(2),
            StoragePath = reader.GetString(3),
            Status = reader.GetString(4)
        };
    }

    public async Task<Dictionary<int, PrototypeRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT assessment_id, project_name, generated_at, storage_path, status FROM assessment_prototypes";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new Dictionary<int, PrototypeRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(0);
            var record = new PrototypeRecord
            {
                AssessmentId = id,
                ProjectName = reader.GetString(1),
                GeneratedAt = reader.GetDateTime(2),
                StoragePath = reader.GetString(3),
                Status = reader.GetString(4)
            };
            results[id] = record;
        }

        return results;
    }

    public async Task DeleteAsync(int assessmentId, CancellationToken cancellationToken = default)
    {
        const string sql = @"DELETE FROM assessment_prototypes WHERE assessment_id=@id";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", assessmentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
