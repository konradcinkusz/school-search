namespace SchoolSearch.Api.Security;

/// <summary>Configuration bound from SchoolSearch:Security (env: SchoolSearch__Security__...).</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "SchoolSearch:Security";

    /// <summary>Requests per minute for a request carrying a valid API key (per key hash). Higher than anonymous.</summary>
    public int FreeKeyPerMinute { get; set; } = 300;

    /// <summary>API keys one client IP may issue per hour. Strict on purpose: issuance is the abuse surface.</summary>
    public int KeyIssuePerIpPerHour { get; set; } = 5;
}
