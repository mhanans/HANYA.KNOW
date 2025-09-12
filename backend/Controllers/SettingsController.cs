using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Middleware;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsStore _settings;

    public SettingsController(SettingsStore settings)
    {
        _settings = settings;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<AppSettings> Get()
    {
        return await _settings.GetAsync();
    }

[HttpPut]
[UiAuthorize("settings")]
public async Task<IActionResult> Put(AppSettings settings)
{    
    await _settings.UpdateAsync(settings);
    return NoContent();
}
}
