using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.RateLimiting;

/// <summary>Named rate-limit policies. Endpoints call <c>RequireRateLimiting</c> with these constants.</summary>
public static class RateLimitPolicies
{
    /// <summary>Parent enquiries (F3 Leads): per client IP per hour, on top of the global per-IP window.</summary>
    public const string Enquiries = "billing-enquiries";

    /// <summary>Authenticated writes: per subject per hour (client IP when anonymous).</summary>
    public const string AuthWrites = "billing-auth-writes";
}

/// <summary>
/// Global limiter, chained with a process-wide concurrency bound. Anonymous callers get a fixed window per client IP
/// (60 per minute by default). Authenticated callers have no per-IP window; their writes are limited per subject by
/// the named policies. Probes are exempt: a 429 on a probe takes the machine out of rotation.
/// </summary>
public static class BillingRateLimitingExtensions
{
    private const string ProbeKey = "probe";
    private const string ProcessKey = "process";
    private const string AuthenticatedKey = "authenticated";
    private const string WebhookKey = "webhook";

    /// <summary>The provider webhook path. It is limited in its own partition, not by the global per-IP window.</summary>
    internal const string WebhookPath = "/api/v1/billing/webhook";

    public static IServiceCollection AddBillingRateLimiting(this IServiceCollection services)
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

            options.AddPolicy<string>(RateLimitPolicies.Enquiries, context =>
            {
                var provider = context.RequestServices;
                var perHour = provider.GetRequiredService<IOptions<BillingOptions>>().Value.RateLimiting.EnquiriesPerIpPerHour;
                var clientIp = provider.GetRequiredService<ClientIpResolver>().Resolve(context);

                return RateLimitPartition.GetFixedWindowLimiter("enquiry:" + clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });

            options.AddPolicy<string>(RateLimitPolicies.AuthWrites, context =>
            {
                var provider = context.RequestServices;
                var perHour = provider.GetRequiredService<IOptions<BillingOptions>>().Value.RateLimiting.AuthWritesPerHour;
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
            var clientIp = services.GetRequiredService<ClientIpResolver>().Resolve(context);

            // The webhook is anonymous and signed. It gets a generous per-address window of its own, so a provider's
            // retries do not exhaust the general allowance (and the general allowance does not starve the webhook).
            if (IsWebhook(context))
            {
                var webhookLimit = services.GetRequiredService<IOptions<BillingOptions>>().Value.RateLimiting.WebhookPerIpPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(WebhookKey + ":" + clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = webhookLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                return RateLimitPartition.GetNoLimiter(AuthenticatedKey);
            }

            var perMinute = services.GetRequiredService<IOptions<BillingOptions>>().Value.RateLimiting.PerIpPerMinute;

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

            var permits = context.RequestServices.GetRequiredService<IOptions<BillingOptions>>()
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

    private static bool IsWebhook(HttpContext context) =>
        string.Equals(context.Request.Path.Value, WebhookPath, StringComparison.OrdinalIgnoreCase);
}
