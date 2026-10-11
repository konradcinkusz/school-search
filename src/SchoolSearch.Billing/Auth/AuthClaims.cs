using System.Security.Claims;

namespace SchoolSearch.Billing.Auth;

/// <summary>
/// Claim names of authservice access tokens (brief §1). The on-wire names of the role claim are UNVERIFIED,
/// so both the short "role" name and the long ClaimTypes.Role URI are accepted. Tokens are read, never minted.
/// </summary>
public static class AuthClaims
{
    public const string SubjectClaim = "sub";
    public const string EmailVerifiedClaim = "email_verified";
    public const string ShortRoleClaim = "role";
    public const string LongRoleClaim = ClaimTypes.Role;

    public const string AdminRole = "Admin";
    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>The authservice user id, or null when the token carries none.</summary>
    public static string? Subject(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(SubjectClaim)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>True when the email_verified claim is boolean true (set at issuance by authservice).</summary>
    public static bool IsEmailVerified(ClaimsPrincipal principal) =>
        principal.FindFirst(EmailVerifiedClaim) is { } claim
        && bool.TryParse(claim.Value, out var verified)
        && verified;

    /// <summary>True when the principal holds one of the given roles in either role claim form.</summary>
    public static bool HasAnyRole(ClaimsPrincipal principal, params string[] roles) =>
        principal.Claims.Any(claim =>
            (claim.Type == ShortRoleClaim || claim.Type == LongRoleClaim)
            && roles.Contains(claim.Value, StringComparer.Ordinal));
}
