using System;
using System.Threading.Tasks;
using backend.Models.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class AiProviderResolver
{
    private readonly AiProviderOptions _options;
    private readonly LlmOptions _defaults;
    private readonly SettingsStore _settings;
    private readonly ILogger<AiProviderResolver> _logger;

    public AiProviderResolver(
        IOptions<AiProviderOptions> options,
        IOptions<LlmOptions> defaults,
        SettingsStore settings,
        ILogger<AiProviderResolver> logger)
    {
        _options = options.Value;
        _defaults = defaults.Value;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ResolvedAiProvider> ResolveAsync(string? process)
    {
        var settings = await _settings.GetAsync().ConfigureAwait(false);
        AiRouteOptions? route = null;
        if (!string.IsNullOrWhiteSpace(process))
        {
            _options.Routes.TryGetValue(process, out route);
        }

        var provider = SelectProvider(route, settings);
        var model = SelectModel(provider, route, settings);
        var apiKey = SelectApiKey(provider, settings);
        var host = SelectHost(provider, settings);

        return new ResolvedAiProvider
        {
            Provider = provider,
            Model = model,
            ApiKey = apiKey,
            Host = host,
            MaxRetries = _defaults.MaxRetries,
            TimeoutSeconds = _defaults.TimeoutSeconds
        };
    }

    private string SelectProvider(AiRouteOptions? route, AppSettings settings)
    {
        var provider = route?.Provider;
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = settings.LlmProvider;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = _options.DefaultProvider;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = _defaults.Provider;
        }

        provider = string.IsNullOrWhiteSpace(provider) ? "openai" : provider.Trim();
        _logger.LogDebug("AI provider for current invocation resolved to {Provider}", provider);
        return provider;
    }

    private string SelectModel(string provider, AiRouteOptions? route, AppSettings settings)
    {
        var providerKey = provider.ToLowerInvariant();
        string? model = route?.Model;

        if (string.IsNullOrWhiteSpace(model))
        {
            model = providerKey switch
            {
                "gemini" => _options.Gemini.Model,
                "ollama" => _options.Ollama.Model,
                "openai" => _options.OpenAi.Model,
                "minimax" => _options.MiniMax.Model,
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = settings.LlmModel;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = _defaults.Model;
        }

        return model?.Trim() ?? string.Empty;
    }

    private string? SelectApiKey(string provider, AppSettings settings)
    {
        var providerKey = provider.ToLowerInvariant();
        string? apiKey = providerKey switch
        {
            "gemini" => _options.Gemini.ApiKey,
            "openai" => _options.OpenAi.ApiKey,
            "minimax" => _options.MiniMax.ApiKey,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.LlmApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _defaults.ApiKey;
        }

        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    private string? SelectHost(string provider, AppSettings settings)
    {
        if (!string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = _options.Ollama.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = settings.OllamaHost;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = _defaults.OllamaHost;
        }

        return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
    }
}

public class ResolvedAiProvider
{
    public string Provider { get; init; } = "openai";
    public string Model { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public string? Host { get; init; }
    public int MaxRetries { get; init; }
    public int TimeoutSeconds { get; init; }
}
