using System.Threading;
using System.Threading.Tasks;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace backend.Controllers;

[ApiController]
[Route("api/source-code")]
[UiAuthorize("source-code")]
public class SourceCodeController : ControllerBase
{
    private readonly SourceCodeSyncService _syncService;
    private readonly ILogger<SourceCodeController> _logger;

    public SourceCodeController(SourceCodeSyncService syncService, ILogger<SourceCodeController> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SourceCodeSyncStatus>> Status(CancellationToken cancellationToken)
    {
        var status = await _syncService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SourceCodeSyncStatus>> Sync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _syncService.SyncAsync(cancellationToken);
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Source code sync request rejected because a job is already running");
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source code sync failed");
            return Problem(detail: ex.Message, statusCode: 500, title: "Source code sync failed");
        }
    }
}
