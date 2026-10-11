namespace SchoolSearch.Api.Security;

/// <summary>
/// Configuration bound from Auth (env: Auth__...). The identity provider is the external authservice (ADR-0015);
/// this service validates its access tokens and never creates them.
/// </summary>
public sealed class AuthServiceOptions
{
    public const string SectionName = "Auth";

    /// <summary>Name under which the integration is reported on /health.</summary>
    public const string IntegrationName = "authservice";

    /// <summary>Base URL of authservice, for example https://school-search-authservice-dev.fly.dev. Unset disables accounts (P8).</summary>
    public string? Authority { get; set; }

    /// <summary>Expected <c>iss</c> claim: the product-specific Jwt__Issuer of authservice. Required with Authority.</summary>
    public string? Issuer { get; set; }

    /// <summary>Expected <c>aud</c> claim: the product-specific Jwt__Audience of authservice. Required with Authority.</summary>
    public string? Audience { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}
