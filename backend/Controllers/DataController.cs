using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Npgsql;
using System.IO;
using System.Text;
using backend.Services;
using ExcelDataReader;
using backend.Middleware;

namespace backend.Controllers;

 [ApiController]
 [Route("api/data")]
 [UiAuthorize("data-sources")]
 public class DataController : ControllerBase
 {
     private readonly LlmClient _llm;

     public DataController(LlmClient llm)
     {
         _llm = llm;
     }
    public class DbConnectionRequest
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 5432;
        public string? Database { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Table { get; set; }
    }

    public class ChatFileRequest
    {
        public IFormFile? File { get; set; }
        public string? Query { get; set; }
    }

    public class ChatDbRequest : DbConnectionRequest
    {
        public string? Query { get; set; }
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

    [HttpPost("chat-db")]
    public async Task<IActionResult> ChatDatabase([FromBody] ChatDbRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return BadRequest("Query is required.");
        if (string.IsNullOrWhiteSpace(req.Host) || string.IsNullOrWhiteSpace(req.Database) || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Table))
            return BadRequest("Connection info and table are required.");

        var connString = $"Host={req.Host};Port={req.Port};Database={req.Database};Username={req.Username};Password={req.Password}";
        try
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            var safeTable = req.Table.All(c => char.IsLetterOrDigit(c) || c == '_') ? req.Table : null;
            if (string.IsNullOrEmpty(safeTable))
                return BadRequest("Invalid table name.");
            var cmdText = $"SELECT * FROM {safeTable} LIMIT 20";
            await using var cmd = new NpgsqlCommand(cmdText, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var sb = new StringBuilder();
            var fieldCount = reader.FieldCount;
            for (int i = 0; i < fieldCount; i++)
                sb.Append(reader.GetName(i)).Append(i == fieldCount - 1 ? '\n' : ',');
            int row = 0;
            while (await reader.ReadAsync() && row < 20)
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    sb.Append(reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString());
                    sb.Append(i == fieldCount - 1 ? '\n' : ',');
                }
                row++;
            }
            var prompt = $"You are a data analyst. Given the following table data:\n{sb}\nQuestion: {req.Query}";
            var answer = await _llm.GenerateAsync(prompt, AiProcesses.DataAnswering);
            return Ok(new { answer });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = true,
                userMessage = "I'm sorry, I was unable to process the database query. Please check your credentials and try again.",
                technicalDetails = ex.Message
            });
        }
    }

    [HttpPost("chat-file")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> ChatFile([FromForm] ChatFileRequest req)
    {
        var file = req.File;
        var query = req.Query;
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Query is required.");

        var sb = new StringBuilder();
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            await using var stream = file.OpenReadStream();
            IExcelDataReader reader;
            if (file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                reader = ExcelReaderFactory.CreateReader(stream);
            else
                reader = ExcelReaderFactory.CreateCsvReader(stream);

            int row = 0;
            while (reader.Read() && row < 50)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    sb.Append(reader.GetValue(i)?.ToString());
                    sb.Append(i == reader.FieldCount - 1 ? '\n' : ',');
                }
                row++;
            }
            reader.Close();
            var prompt = $"You are a data analyst. Given the following data:\n{sb}\nQuestion: {query}";
            var answer = await _llm.GenerateAsync(prompt, AiProcesses.DataAnalysis);
            return Ok(new { answer });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = true,
                userMessage = "I'm sorry, I was unable to process the uploaded Excel file. It might be corrupted or in an unsupported format. Please try uploading a different file.",
                technicalDetails = ex.Message
            });
        }
    }
}
