namespace SchoolSearch.Reviews.Auth;

/// <summary>
/// Configuration bound from the Auth section (env: Auth__...). These are public identifiers, never secrets.
/// authservice is the only identity provider (ADR-0015). This service validates its tokens and mints none.
/// </summary>
public sealed class AuthServiceOptions
{
    public const string SectionName = "Auth";

    /// <summary>authservice base URL, for example https://myproject-authservice.fly.dev (no path).</summary>
    public string? Authority { get; set; }

    /// <summary>Expected issuer, equal to authservice Jwt__Issuer. Product-specific; the authservice default is refused.</summary>
    public string? Issuer { get; set; }

    /// <summary>Expected audience, equal to authservice Jwt__Audience. Product-specific; the authservice default is refused.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// True when token validation can be set up. Otherwise the service starts, auth-gated endpoints answer
    /// 503 auth_unavailable and /health lists the integration "authservice" as disabled (P8).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && IsProductValue(Issuer)
        && IsProductValue(Audience);

    private static bool IsProductValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "AuthService", StringComparison.OrdinalIgnoreCase);
}
