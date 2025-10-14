using System.Net.Http;
using System.Security.Claims;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/github")]
[UiAuthorize("source-code")]
public class GitHubController : ControllerBase
{
    private readonly GitHubIntegrationService _github;
    private readonly ILogger<GitHubController> _logger;

    public GitHubController(GitHubIntegrationService github, ILogger<GitHubController> logger)
    {
        _github = github;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<GitHubStatus>> Status(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var status = await _github.GetStatusAsync(userId, cancellationToken);
        return Ok(status);
    }

    [HttpGet("login")]
    public async Task<IActionResult> LoginUrl(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var url = await _github.CreateLoginUrlAsync(userId);
            return Ok(new { url });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "GitHub login requested but OAuth is not configured");
            return Problem(detail: ex.Message, statusCode: 400, title: "GitHub is not configured");
        }
    }

    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] GitHubExchangeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.State))
            return BadRequest(new { message = "Code and state are required." });

        try
        {
            var userId = GetUserId();
            await _github.CompleteLoginAsync(userId, request.State, request.Code, cancellationToken);
            var status = await _github.GetStatusAsync(userId, cancellationToken);
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "GitHub OAuth exchange failed");
            return Problem(detail: ex.Message, statusCode: 400, title: "GitHub exchange failed");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GitHub OAuth exchange request failed");
            return Problem(detail: ex.Message, statusCode: 502, title: "GitHub exchange failed");
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        await _github.DisconnectAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("repos")]
    public async Task<IActionResult> Repositories(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            var repos = await _github.ListRepositoriesAsync(userId, cancellationToken);
            return Ok(repos);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: 400, title: "GitHub is not connected");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub repositories");
            return Problem(detail: ex.Message, statusCode: 502, title: "GitHub request failed");
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

public record GitHubExchangeRequest
{
    public string Code { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}
