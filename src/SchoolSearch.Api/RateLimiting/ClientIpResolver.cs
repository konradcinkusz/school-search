using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Options;

namespace SchoolSearch.Api.RateLimiting;

/// <summary>
/// The one place the client address is resolved. The limiter and any future per-client
/// allowance both call this, so they meter the same visitor.
/// </summary>
public sealed class ClientIpResolver(IOptions<CatalogApiOptions> options)
{
    private const string FlyClientIpHeader = "Fly-Client-IP";

    public string Resolve(HttpContext context)
    {
        // Every TCP peer behind Fly's edge is the proxy, so the socket address alone would put the whole
        // internet in one bucket. The header is trusted only when configuration says a proxy is in front.
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
}
