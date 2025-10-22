namespace backend.Models.Configuration;

public class AccelistSsoOptions
{
    public const string SectionName = "AccelistSso";

    public string Host { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
