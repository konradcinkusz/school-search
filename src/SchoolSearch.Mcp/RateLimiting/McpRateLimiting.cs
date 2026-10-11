using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolSearch.Mcp.Options;

namespace SchoolSearch.Mcp.RateLimiting;

/// <summary>
/// One fixed window per client IP for the whole application (the MCP endpoint and the Api-style error body).
/// Health and liveness are exempt: a 429 on a probe takes the machine out of rotation. Rejections are immediate
/// (no queue) with Retry-After and the uniform error shape.
/// </summary>
public static class McpRateLimiting
{
    private const string FlyClientIpHeader = "Fly-Client-IP";
    private const string ProbeKey = "probe";

    public static IServiceCollection AddMcpRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    context.HttpContext.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(new { error = new { code = "rate_limited", message = "Too many requests. Retry later." } }),
                    token);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.Value is "/health" or "/alive")
                {
                    return RateLimitPartition.GetNoLimiter(ProbeKey);
                }

                var mcpOptions = context.RequestServices.GetRequiredService<IOptions<McpOptions>>().Value;
                var clientIp = ResolveClientIp(context, mcpOptions);

                return RateLimitPartition.GetFixedWindowLimiter("ip:" + clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = mcpOptions.PerIpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    /// <summary>
    /// The socket peer, unless a proxy is configured to be in front: then Fly-Client-IP. Trusting the header without
    /// a proxy would let every caller choose its own bucket, which is why it is opt-in configuration.
    /// </summary>
    private static string ResolveClientIp(HttpContext context, McpOptions options)
    {
        if (options.TrustProxyClientIpHeader
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
