using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System.IO;

namespace backend.Controllers;

[ApiController]
[Route("api/data")]
public class DataController : ControllerBase
{
    public class DbConnectionRequest
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 5432;
        public string? Database { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Table { get; set; }
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] DbConnectionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Host) || string.IsNullOrWhiteSpace(req.Database) || string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { success = false, message = "Host, database and username are required." });

        var connString = $"Host={req.Host};Port={req.Port};Database={req.Database};Username={req.Username};Password={req.Password}";
        try
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            if (!string.IsNullOrWhiteSpace(req.Table))
            {
                await using var cmd = new NpgsqlCommand("SELECT 1 FROM information_schema.tables WHERE table_name = @p", conn);
                cmd.Parameters.AddWithValue("p", req.Table);
                var exists = await cmd.ExecuteScalarAsync();
                if (exists == null)
                    return BadRequest(new { success = false, message = $"Table '{req.Table}' not found." });
            }
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("upload-excel")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only .xlsx or .csv files are supported.");

        // For now just accept the file and discard the contents.
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return Ok(new { success = true });
    }
}
