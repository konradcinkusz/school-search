using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Auth;

namespace SchoolSearch.Reviews.Tests;

/// <summary>
/// Test-only scheme that issues claims from request headers: X-Test-Sub (absent means anonymous),
/// X-Test-Email-Verified (true/false) and X-Test-Role (comma separated). Never registered by production code.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestBearer";
    public const string SubHeader = "X-Test-Sub";
    public const string EmailVerifiedHeader = "X-Test-Email-Verified";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new Claim(AuthClaims.SubjectClaim, sub.ToString()) };

        if (Request.Headers.TryGetValue(EmailVerifiedHeader, out var verified))
        {
            claims.Add(new Claim(AuthClaims.EmailVerifiedClaim, verified.ToString()));
        }

        var roles = Request.Headers[RoleHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var role in roles)
        {
            claims.Add(new Claim(AuthClaims.ShortRoleClaim, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
