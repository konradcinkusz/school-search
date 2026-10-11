using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Registers the parent-enquiry slice. Called from Program.cs (WIRING-LEADS). Expects BillingDbContext to be
/// registered already. Named rate-limit policies are added here; the global limiter and the rejection shape belong
/// to the billing core.
/// </summary>
public static class LeadServiceCollectionExtensions
{
    public static IServiceCollection AddLeadServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LeadOptions>().Bind(configuration.GetSection(LeadOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddRateLimiter(options =>
        {
            // Anonymous enquiries: per client IP per hour (SPEC-F: 5).
            options.AddPolicy<string>(LeadRules.EnquiryRateLimitPolicy, context =>
            {
                var provider = context.RequestServices;
                var perHour = Math.Clamp(provider.GetRequiredService<IOptions<LeadOptions>>().Value.EnquiriesPerIpPerHour, 1, 100);
                var clientIp = LeadAccess.ClientIp(context, provider.GetRequiredService<IConfiguration>());

                return RateLimitPartition.GetFixedWindowLimiter("leads-enquiry:" + clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });

            // Owner writes: per subject per hour, client IP when the call is anonymous (SPEC-F: 20).
            options.AddPolicy<string>(LeadRules.OwnerWriteRateLimitPolicy, context =>
            {
                var provider = context.RequestServices;
                var perHour = Math.Clamp(provider.GetRequiredService<IOptions<LeadOptions>>().Value.OwnerWritesPerHour, 1, 1000);
                var subject = LeadAccess.Subject(context.User);
                var key = subject is null
                    ? "ip:" + LeadAccess.ClientIp(context, provider.GetRequiredService<IConfiguration>())
                    : "sub:" + LeadAccess.Digest(subject);

                return RateLimitPartition.GetFixedWindowLimiter("leads-owner:" + key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });
        });

        services.AddHostedService<LeadPurgeService>();

        return services;
    }
}
