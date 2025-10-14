using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services;

public class GitHubIntegrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GitHubOptions _options;
    private readonly GitHubTokenStore _tokens;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SourceCodeSyncService _sourceCodeSync;
    private readonly ILogger<GitHubIntegrationService> _logger;

    public GitHubIntegrationService(
        IOptions<GitHubOptions> options,
        GitHubTokenStore tokens,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        SourceCodeSyncService sourceCodeSync,
        ILogger<GitHubIntegrationService> logger)
    {
        _options = options.Value;
        _tokens = tokens;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _sourceCodeSync = sourceCodeSync;
        _logger = logger;
    }

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) &&
        !string.IsNullOrWhiteSpace(_options.ClientSecret) &&
        !string.IsNullOrWhiteSpace(_options.RedirectUri);

    public async Task<GitHubStatus> GetStatusAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GitHubStatus(false, false, null, null);
        }

        var token = await _tokens.GetAsync(userId, cancellationToken);
        if (token == null)
            return new GitHubStatus(true, false, null, null);

        try
        {
            var user = await GetCurrentUserAsync(token, cancellationToken);
            return new GitHubStatus(true, true, user?.Login, user?.AvatarUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub user for status check");
            return new GitHubStatus(true, false, null, null);
        }
    }

    public Task<string> CreateLoginUrlAsync(int userId)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("GitHub OAuth is not configured.");

        var state = GenerateStateToken();
        _cache.Set(state, userId, TimeSpan.FromMinutes(10));
        var scopes = (_options.Scopes != null && _options.Scopes.Length > 0)
            ? string.Join(' ', _options.Scopes)
            : "repo read:user";
        var builder = new StringBuilder("https://github.com/login/oauth/authorize?");
        builder.Append("client_id=").Append(Uri.EscapeDataString(_options.ClientId!));
        builder.Append("&redirect_uri=").Append(Uri.EscapeDataString(_options.RedirectUri!));
        builder.Append("&scope=").Append(Uri.EscapeDataString(scopes));
        builder.Append("&state=").Append(Uri.EscapeDataString(state));
        return Task.FromResult(builder.ToString());
    }

    public async Task CompleteLoginAsync(int userId, string state, string code, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("GitHub OAuth is not configured.");

        if (!_cache.TryGetValue<int>(state, out var cachedUserId) || cachedUserId != userId)
            throw new InvalidOperationException("Invalid or expired GitHub login state.");

        _cache.Remove(state);

        var token = await ExchangeCodeAsync(code, cancellationToken);
        await _tokens.SaveAsync(userId, token, cancellationToken);
    }

    public async Task DisconnectAsync(int userId, CancellationToken cancellationToken = default)
        => await _tokens.DeleteAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<GitHubRepository>> ListRepositoriesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var token = await RequireTokenAsync(userId, cancellationToken);
        using var client = CreateApiClient(token);
        using var response = await client.GetAsync("user/repos?per_page=100&type=all&sort=updated", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var repos = await JsonSerializer.DeserializeAsync<List<GitHubRepository>>(stream, JsonOptions, cancellationToken);
        return repos ?? new List<GitHubRepository>();
    }

    public async Task ImportRepositoryAsync(int userId, string repository, string? branch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
            throw new ArgumentException("Repository is required", nameof(repository));

        var token = await RequireTokenAsync(userId, cancellationToken);
        var targetDir = _sourceCodeSync.GetSourceRoot();
        Directory.CreateDirectory(targetDir);

        var tempFile = Path.GetTempFileName();
        var tempDir = Path.Combine(Path.GetTempPath(), "hanya-github-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var archiveUrl = new StringBuilder("repos/").Append(repository).Append("/zipball");
            if (!string.IsNullOrWhiteSpace(branch))
                archiveUrl.Append('/').Append(branch);

            using var client = CreateApiClient(token);
            using var response = await client.GetAsync(archiveUrl.ToString(), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var fileStream = File.Create(tempFile))
            {
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            ZipFile.ExtractToDirectory(tempFile, tempDir, overwriteFiles: true);
            var extractedRoot = Directory.GetDirectories(tempDir).FirstOrDefault() ?? tempDir;

            ClearDirectory(targetDir);
            CopyDirectory(extractedRoot, targetDir);
            _logger.LogInformation("Imported repository {Repo} into {TargetDir}", repository, targetDir);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // ignore cleanup failures
            }

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private async Task<GitHubToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("GitHubOAuth");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId!,
            ["client_secret"] = _options.ClientSecret!,
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri!
        });
        using var response = await client.PostAsync("login/oauth/access_token", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(JsonOptions, cancellationToken);
        if (payload == null || string.IsNullOrWhiteSpace(payload.AccessToken))
            throw new InvalidOperationException("GitHub did not return an access token.");

        return new GitHubToken
        {
            AccessToken = payload.AccessToken!,
            TokenType = payload.TokenType ?? "bearer",
            Scope = payload.Scope ?? string.Empty
        };
    }

    private async Task<GitHubUser?> GetCurrentUserAsync(GitHubToken token, CancellationToken cancellationToken)
    {
        using var client = CreateApiClient(token);
        using var response = await client.GetAsync("user", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubUser>(stream, JsonOptions, cancellationToken);
    }

    private async Task<GitHubToken> RequireTokenAsync(int userId, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync(userId, cancellationToken);
        if (token == null)
            throw new InvalidOperationException("GitHub account is not connected.");
        return token;
    }

    private HttpClient CreateApiClient(GitHubToken token)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static void ClearDirectory(string directory)
    {
        var dirInfo = new DirectoryInfo(directory);
        if (!dirInfo.Exists)
            return;

        foreach (var file in dirInfo.GetFiles())
        {
            file.IsReadOnly = false;
            file.Delete();
        }

        foreach (var dir in dirInfo.GetDirectories())
        {
            dir.Delete(true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        Directory.CreateDirectory(destinationDir);

        foreach (var file in source.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (var subDir in source.GetDirectories())
        {
            var targetSubDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, targetSubDir);
        }
    }

    private static string GenerateStateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private record GitHubTokenResponse
    {
        public string? AccessToken { get; init; }
        public string? Scope { get; init; }
        public string? TokenType { get; init; }
    }

    private record GitHubUser
    {
        public string? Login { get; init; }
        public string? AvatarUrl { get; init; }
    }
}

public record GitHubStatus(bool IsConfigured, bool IsConnected, string? Login, string? AvatarUrl);

public record GitHubRepository
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Private { get; init; }
    public string DefaultBranch { get; init; } = "main";
}
