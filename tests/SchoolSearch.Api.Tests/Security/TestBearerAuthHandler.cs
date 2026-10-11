using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// Test-only stand-in for the authservice JWT scheme. It never validates a signature: it reads
/// <c>Authorization: Bearer test:&lt;sub&gt;:&lt;roles&gt;</c> (role claim under the wire name <c>role</c>) or
/// <c>Bearer testlong:&lt;sub&gt;:&lt;roles&gt;</c> (role claim under the .NET long name). Any other value is "no result",
/// so the admin filter answers 401 exactly as it does for a token it cannot read. Registered only by ConfiguredApiFactory.
/// </summary>
public sealed class TestBearerAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestBearer";

    private const string WireRolePrefix = "Bearer test:";
    private const string LongRolePrefix = "Bearer testlong:";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();

        string roleClaimType;
        string rest;
        if (header.StartsWith(WireRolePrefix, StringComparison.Ordinal))
        {
            roleClaimType = "role";
            rest = header[WireRolePrefix.Length..];
        }
        else if (header.StartsWith(LongRolePrefix, StringComparison.Ordinal))
        {
            roleClaimType = ClaimTypes.Role;
            rest = header[LongRolePrefix.Length..];
        }
        else
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = rest.Split(':');
        if (parts.Length != 2)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed test token."));
        }

        var claims = new List<Claim>();
        if (parts[0].Length > 0)
        {
            claims.Add(new Claim("sub", parts[0]));
        }

        claims.AddRange(parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(roleClaimType, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", roleClaimType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
