using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.Tests;

/// <summary>
/// Test-only scheme that issues claims from request headers: X-Test-Sub (absent means anonymous) and
/// X-Test-Email-Verified (true/false). Registered by the Leads test factory only.
/// </summary>
public sealed class LeadsTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LeadsTestBearer";
    public const string SubHeader = "X-Test-Sub";
    public const string EmailVerifiedHeader = "X-Test-Email-Verified";

    private const string EmailVerifiedClaim = "email_verified";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new Claim(LeadRules.SubjectClaim, sub.ToString()) };
        if (Request.Headers.TryGetValue(EmailVerifiedHeader, out var verified))
        {
            claims.Add(new Claim(EmailVerifiedClaim, verified.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
