using System.Collections.Generic;
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
    private readonly ProjectTemplateStore _templates;

    public PresalesConfigurationController(PresalesConfigurationStore store, ProjectTemplateStore templates)
    {
        _store = store;
        _templates = templates;
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

    [HttpGet("tasks")]
    [UiAuthorize("pre-sales-configuration")]
    public async Task<ActionResult<IEnumerable<string>>> ListTasks()
    {
        var tasks = await _templates.ListEstimationColumnsAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(tasks);
    }
}
