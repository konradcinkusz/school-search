using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.RateLimiting;

/// <summary>
/// The one place the client address is resolved. The limiters and any future per-client allowance call this,
/// so they meter the same visitor. The BFF forwards the visitor address in X-Client-Ip together with a shared secret.
/// </summary>
public sealed class ClientIpResolver(IOptions<BillingOptions> options, IConfiguration configuration)
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

    private string? ResolveFromBff(HttpContext context)
    {
        var secret = configuration["BFF_SHARED_SECRET"];
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        if (!context.Request.Headers.TryGetValue(BffSecretHeader, out var presented)
            || !context.Request.Headers.TryGetValue(BffClientIpHeader, out var ip))
        {
            return null;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented.ToString()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return null;
        }

        return IPAddress.TryParse(ip.ToString().Trim(), out var parsed) ? parsed.ToString() : null;
    }
}
