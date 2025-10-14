namespace backend.Services;

public class GitHubOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }
    public string[]? Scopes { get; set; }
}
