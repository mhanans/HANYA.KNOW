using System.Threading.Tasks;
using backend.Models;
using backend.Middleware;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/presales/config")]
public class PresalesConfigurationController : ControllerBase
{
    private readonly PresalesConfigurationStore _store;

    public PresalesConfigurationController(PresalesConfigurationStore store)
    {
        _store = store;
    }

    [HttpGet]
    [UiAuthorize("pre-sales-configuration")]
    public async Task<ActionResult<PresalesConfiguration>> GetConfiguration()
    {
        var config = await _store.GetConfigurationAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(config);
    }

    [HttpPut]
    [UiAuthorize("pre-sales-configuration")]
    public async Task<ActionResult<PresalesConfiguration>> SaveConfiguration(PresalesConfiguration configuration)
    {
        var result = await _store.SaveConfigurationAsync(configuration, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(result);
    }
}
