using System;
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
    public async Task<IActionResult> Put([FromBody] AppSettings? settings)
    {
        if (settings is null)
            return BadRequest("Settings payload is required.");

        settings.ApplicationName = string.IsNullOrWhiteSpace(settings.ApplicationName)
            ? null
            : settings.ApplicationName.Trim();
        settings.LogoUrl = string.IsNullOrWhiteSpace(settings.LogoUrl)
            ? null
            : settings.LogoUrl.Trim();

        var provider = settings.LlmProvider?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(provider) && provider is not ("openai" or "gemini" or "ollama" or "minimax"))
            return BadRequest("Unsupported LLM provider.");
        settings.LlmProvider = string.IsNullOrEmpty(provider) ? null : provider;

        settings.LlmModel = string.IsNullOrWhiteSpace(settings.LlmModel)
            ? null
            : settings.LlmModel.Trim();
        settings.LlmApiKey = string.IsNullOrWhiteSpace(settings.LlmApiKey)
            ? null
            : settings.LlmApiKey.Trim();
        settings.OllamaHost = string.IsNullOrWhiteSpace(settings.OllamaHost)
            ? null
            : settings.OllamaHost.Trim();

        if (settings.LlmProvider == "ollama")
        {
            if (string.IsNullOrWhiteSpace(settings.LlmModel))
                return BadRequest("Ollama model is required.");
            if (string.IsNullOrWhiteSpace(settings.OllamaHost))
                return BadRequest("Ollama host is required.");
            if (!Uri.TryCreate(settings.OllamaHost, UriKind.Absolute, out var hostUri) ||
                (hostUri.Scheme != Uri.UriSchemeHttp && hostUri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest("Ollama host must be an absolute HTTP or HTTPS URL.");
            }
            if (!string.IsNullOrEmpty(hostUri.UserInfo))
                return BadRequest("Ollama host must not include credentials in the URL.");

            settings.OllamaHost = hostUri.GetLeftPart(UriPartial.Authority);
        }
        else if (settings.LlmProvider is "openai" or "gemini" or "minimax")
        {
            if (string.IsNullOrWhiteSpace(settings.LlmModel))
                return BadRequest("Model is required for the selected provider.");
            if (string.IsNullOrWhiteSpace(settings.LlmApiKey))
                return BadRequest("API key is required for the selected provider.");
        }

        await _settings.UpdateAsync(settings);
        return NoContent();
    }
}
