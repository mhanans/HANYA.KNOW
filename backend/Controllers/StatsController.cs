using backend.Services;
using Microsoft.AspNetCore.Mvc;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/stats")]
[UiAuthorize("dashboard")]
public class StatsController : ControllerBase
{
    private readonly StatsStore _store;

    public StatsController(StatsStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardStats>> Get()
    {
        var stats = await _store.GetStatsAsync();
        return Ok(stats);
    }
}

