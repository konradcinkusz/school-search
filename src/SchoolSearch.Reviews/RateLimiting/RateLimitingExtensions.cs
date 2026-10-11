using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Errors;
using SchoolSearch.Reviews.Settings;

namespace SchoolSearch.Reviews.RateLimiting;

/// <summary>Named rate-limit policies. Endpoints call <c>RequireRateLimiting</c> with these constants.</summary>
public static class RateLimitPolicies
{
    /// <summary>Notices (DSA art. 16): per client IP per hour. Used by the notice endpoints (E1b).</summary>
    public const string Notices = "reviews-notices";

    /// <summary>Authenticated writes: per subject per hour (client IP when anonymous).</summary>
    public const string AuthWrites = "reviews-auth-writes";
}

/// <summary>
/// Global limiter, chained with a process-wide concurrency bound. Anonymous callers get a fixed window per client IP
/// (60 per minute by default). Authenticated callers have no per-IP window; their writes are limited per subject by
/// the named policies. Probes are exempt: a 429 on a probe takes the machine out of rotation.
/// </summary>
public static class ReviewsRateLimitingExtensions
{
    private const string ProbeKey = "probe";
    private const string ProcessKey = "process";
    private const string AuthenticatedKey = "authenticated";
    private const string TrustedServiceKey = "trusted-service";

    public static IServiceCollection AddReviewsRateLimiting(this IServiceCollection services)
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

            options.AddPolicy<string>(RateLimitPolicies.Notices, context =>
            {
                var provider = context.RequestServices;
                var perHour = provider.GetRequiredService<IOptions<ReviewsOptions>>().Value.RateLimiting.NoticesPerIpPerHour;
                var clientIp = provider.GetRequiredService<ClientIpResolver>().Resolve(context);

                return RateLimitPartition.GetFixedWindowLimiter("notice:" + clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });

            options.AddPolicy<string>(RateLimitPolicies.AuthWrites, context =>
            {
                var provider = context.RequestServices;
                var perHour = provider.GetRequiredService<IOptions<ReviewsOptions>>().Value.RateLimiting.AuthWritesPerHour;
                var subject = AuthClaims.Subject(context.User);
                var key = subject is null
                    ? "ip:" + provider.GetRequiredService<ClientIpResolver>().Resolve(context)
                    : "sub:" + provider.GetRequiredService<SubjectHasher>().Hash(subject);

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    private static PartitionedRateLimiter<HttpContext> PerClientWindow() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (IsProbe(context))
            {
                return RateLimitPartition.GetNoLimiter(ProbeKey);
            }

            var services = context.RequestServices;
            var resolver = services.GetRequiredService<ClientIpResolver>();

            // A trusted service (valid shared secret, no visitor address) gets its own bucket, never a visitor's.
            if (resolver.IsTrustedServiceCall(context))
            {
                var serviceLimit = services.GetRequiredService<IOptions<ReviewsOptions>>().Value.RateLimiting.TrustedServicePerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(TrustedServiceKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = serviceLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                return RateLimitPartition.GetNoLimiter(AuthenticatedKey);
            }

            var perMinute = services.GetRequiredService<IOptions<ReviewsOptions>>().Value.RateLimiting.PerIpPerMinute;
            var clientIp = resolver.Resolve(context);

            return RateLimitPartition.GetFixedWindowLimiter("ip:" + clientIp, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
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

            var permits = context.RequestServices.GetRequiredService<IOptions<ReviewsOptions>>()
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
