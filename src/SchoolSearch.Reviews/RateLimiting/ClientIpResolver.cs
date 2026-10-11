using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Settings;

namespace SchoolSearch.Reviews.RateLimiting;

/// <summary>
/// The one place the client address is resolved. The limiters and any future per-client allowance call this,
/// so they meter the same visitor. The BFF (and the MCP service) forwards the visitor address in X-Client-Ip together
/// with a shared secret. A call that carries the secret but no visitor address is a trusted service call.
/// </summary>
public sealed class ClientIpResolver(IOptions<ReviewsOptions> options, IConfiguration configuration)
{
    private const string FlyClientIpHeader = "Fly-Client-IP";
    private const string BffClientIpHeader = "X-Client-Ip";
    private const string BffSecretHeader = "X-Bff-Secret";

    public string Resolve(HttpContext context)
    {
        // The web BFF calls this service server-side, so the socket peer is the BFF machine. The address is honoured
        // only when the shared secret matches (constant-time), so nobody else can pick their own bucket.
        var forwarded = ResolveFromBff(context);
        if (forwarded is not null)
        {
            return forwarded;
        }

        // Behind Fly's edge every TCP peer is the proxy. The header is trusted only when configuration says so.
        if (options.Value.TrustProxyClientIpHeader
            && context.Request.Headers.TryGetValue(FlyClientIpHeader, out var header))
        {
            var value = header.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// True when the caller presents the valid shared secret and no visitor address. Such a call is another service
    /// acting for itself (for example, the MCP service with no visitor in its request context). It gets its own bucket
    /// so one busy service cannot starve the visitor buckets, and it cannot pick a visitor bucket either.
    /// </summary>
    public bool IsTrustedServiceCall(HttpContext context) =>
        SecretMatches(context) && !context.Request.Headers.ContainsKey(BffClientIpHeader);

    private string? ResolveFromBff(HttpContext context)
    {
        if (!SecretMatches(context) || !context.Request.Headers.TryGetValue(BffClientIpHeader, out var ip))
        {
            return null;
        }

        return IPAddress.TryParse(ip.ToString().Trim(), out var parsed) ? parsed.ToString() : null;
    }

    private bool SecretMatches(HttpContext context)
    {
        var secret = configuration["BFF_SHARED_SECRET"];
        if (string.IsNullOrEmpty(secret) || !context.Request.Headers.TryGetValue(BffSecretHeader, out var presented))
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented.ToString()));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
