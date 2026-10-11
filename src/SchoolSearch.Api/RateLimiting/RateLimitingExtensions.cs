using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Options;
using SchoolSearch.Api.Security;

namespace SchoolSearch.Api.RateLimiting;

/// <summary>
/// Two layers, chained: a fixed window per caller (valid API key digest, otherwise resolved client IP),
/// then a process-wide concurrency bound. No accounts exist yet (Phase E).
/// Rejections are immediate (QueueLimit = 0) and use the uniform error shape with Retry-After.
/// Health and liveness probes are exempt: a 429 on a probe takes the machine out of rotation.
/// </summary>
public static class RateLimitingExtensions
{
    private const string ProbeKey = "probe";
    private const string ProcessKey = "process";

    public static IServiceCollection AddCatalogRateLimiting(this IServiceCollection services)
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

                await ApiErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry later.", token);
            };

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(PerClientWindow(), ProcessConcurrency());
        });

        return services;
    }

    /// <summary>
    /// One window per caller. A request with an active API key (resolved by <c>ApiKeyResolutionMiddleware</c>
    /// before this limiter runs) uses the key's digest and the higher free-tier limit; every other request uses
    /// its client IP and the anonymous limit. The two namespaces are prefixed so an IP can never collide with a digest.
    /// </summary>
    private static PartitionedRateLimiter<HttpContext> PerClientWindow() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (IsProbe(context))
            {
                return RateLimitPartition.GetNoLimiter(ProbeKey);
            }

            var services = context.RequestServices;

            if (context.Items.TryGetValue(ApiKeyBucket.ItemKey, out var item) && item is ApiKeyBucket bucket)
            {
                var freePerMinute = services.GetRequiredService<IOptions<SecurityOptions>>().Value.FreeKeyPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter("key:" + bucket.KeyHash, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = freePerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            }

            var limits = services.GetRequiredService<IOptions<CatalogApiOptions>>().Value.RateLimiting;
            var clientIp = services.GetRequiredService<ClientIpResolver>().Resolve(context);

            return RateLimitPartition.GetFixedWindowLimiter("ip:" + clientIp, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PerIpPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });

    private static PartitionedRateLimiter<HttpContext> ProcessConcurrency() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (IsProbe(context))
            {
                return RateLimitPartition.GetNoLimiter(ProbeKey);
            }

            var permits = context.RequestServices.GetRequiredService<IOptions<CatalogApiOptions>>()
                .Value.RateLimiting.ConcurrentRequests;

            return RateLimitPartition.GetConcurrencyLimiter(ProcessKey, _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = permits,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        });

    private static bool IsProbe(HttpContext context) =>
        context.Request.Path.Value is "/health" or "/alive";
}
