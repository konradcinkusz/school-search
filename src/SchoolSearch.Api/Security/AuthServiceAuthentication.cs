using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Validates RS256 access tokens issued by authservice: keys come from its discovery document and JWKS, issuer and
/// audience are pinned, and clock skew is zero. Validation only: this service has no user store, no password
/// handling and no token creation (ADR-0015, enforced by the architecture tests).
/// Without <c>Auth:Authority</c> no scheme is registered and the admin group answers 404 (P8).
/// The authorization rules are duplicated from the Reviews service on purpose; see ADR-0015.
/// </summary>
public static class AuthServiceAuthentication
{
    /// <summary>Role claim as authservice writes it on the wire (its ClaimTypes.Role is serialised as this name).</summary>
    public const string RoleClaimType = "role";

    /// <summary>Subject claim: the authservice user id.</summary>
    public const string SubjectClaimType = "sub";

    /// <summary>Roles that may use the admin surface.</summary>
    public static readonly string[] AdminRoles = ["Admin", "SuperAdmin"];

    public static IServiceCollection AddAuthServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<AuthServiceOptions>(configuration.GetSection(AuthServiceOptions.SectionName));

        var options = configuration.GetSection(AuthServiceOptions.SectionName).Get<AuthServiceOptions>() ?? new AuthServiceOptions();
        if (!options.IsConfigured)
        {
            return services;
        }

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Auth:Authority must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Auth:Issuer and Auth:Audience are required when Auth:Authority is set.");
        }

        var metadataAddress = options.Authority!.TrimEnd('/') + "/.well-known/openid-configuration";
        var issuer = options.Issuer;
        var audience = options.Audience;
        var requireHttps = !environment.IsDevelopment();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
            {
                jwt.MetadataAddress = metadataAddress;
                jwt.RequireHttpsMetadata = requireHttps;

                // Claim names stay as authservice writes them; no legacy inbound mapping to long-form names.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RoleClaimType = RoleClaimType,
                    NameClaimType = SubjectClaimType,
                };
            });

        return services;
    }

    /// <summary>
    /// True when the principal carries an Admin or SuperAdmin role and a subject. The role may arrive under the
    /// short wire name or the long .NET claim type, because the wire name is not verified yet (ADR-0015).
    /// </summary>
    public static bool IsAdmin(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
        && principal.HasClaim(c => c.Type is RoleClaimType or ClaimTypes.Role && AdminRoles.Contains(c.Value, StringComparer.Ordinal))
        && principal.HasClaim(c => c.Type == SubjectClaimType && !string.IsNullOrWhiteSpace(c.Value));
}
