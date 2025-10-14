using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Security.Claims;
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
    private readonly GitHubIntegrationService _github;

    public SourceCodeController(SourceCodeSyncService syncService, GitHubIntegrationService github, ILogger<SourceCodeController> logger)
    {
        _syncService = syncService;
        _github = github;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SourceCodeSyncStatus>> Status(CancellationToken cancellationToken)
    {
        var status = await _syncService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SourceCodeSyncStatus>> Sync([FromBody] SourceCodeSyncRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            if (request != null && !string.IsNullOrWhiteSpace(request.GitHubRepository))
            {
                var userId = GetUserId();
                await _github.ImportRepositoryAsync(userId, request.GitHubRepository, request.Branch, cancellationToken);
            }
            var status = await _syncService.StartSyncInBackgroundAsync(cancellationToken);
            return Accepted(status);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Source code sync request rejected because a job is already running");
            return Conflict(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to import GitHub repository before sync");
            return Problem(detail: ex.Message, statusCode: 502, title: "GitHub import failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source code sync failed");
            return Problem(detail: ex.Message, statusCode: 500, title: "Source code sync failed");
        }
    }

    private int GetUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Missing user identifier.");
        return int.Parse(id);
    }
}

public record SourceCodeSyncRequest
{
    public string? GitHubRepository { get; init; }
    public string? Branch { get; init; }
}
