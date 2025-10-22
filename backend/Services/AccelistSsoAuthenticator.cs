using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class AccelistSsoAuthenticator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AccelistSsoOptions _options;
    private readonly ILogger<AccelistSsoAuthenticator> _logger;

    public AccelistSsoAuthenticator(
        IHttpClientFactory httpClientFactory,
        IOptions<AccelistSsoOptions> options,
        ILogger<AccelistSsoAuthenticator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetEmailFromTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _logger.LogWarning("Received empty TAMSignOnToken for SSO login.");
            return null;
        }

        AccelistSsoTokenResponse? parsedToken;
        try
        {
            parsedToken = JsonSerializer.Deserialize<AccelistSsoTokenResponse>(rawToken, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse TAMSignOnToken payload.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsedToken?.AccessToken))
        {
            _logger.LogWarning("SSO token did not contain an access_token.");
            return null;
        }

        var host = (_options.Host ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrEmpty(host))
        {
            _logger.LogWarning("Accelist SSO host is not configured. Unable to validate token.");
            return null;
        }

        var client = _httpClientFactory.CreateClient("AccelistSso");
        if (client.BaseAddress == null)
        {
            client.BaseAddress = new Uri(host + "/");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parsedToken.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Accelist SSO userinfo endpoint.");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Accelist SSO userinfo endpoint returned status {StatusCode}.", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        AccelistSsoUserInfoResponse? userInfo;
        try
        {
            userInfo = await JsonSerializer.DeserializeAsync<AccelistSsoUserInfoResponse>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Accelist SSO userinfo response.");
            return null;
        }

        var email = !string.IsNullOrWhiteSpace(userInfo?.Email) ? userInfo!.Email : userInfo?.Sub ?? userInfo?.PreferredUsername;
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Accelist SSO userinfo response did not contain an email or subject identifier.");
            return null;
        }

        return email;
    }

    private sealed record AccelistSsoTokenResponse
    {
        public string? AccessToken { get; init; }
    }

    private sealed record AccelistSsoUserInfoResponse
    {
        public string? Email { get; init; }
        public string? Sub { get; init; }
        public string? PreferredUsername { get; init; }
    }
}
