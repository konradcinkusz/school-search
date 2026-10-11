using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.RateLimiting;

namespace SchoolSearch.Api.Security;

/// <summary>Registers API keys, the admin credential and the key-issuance rate policy. Called from Program.cs.</summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ApiKeyService>();
        services.AddSingleton(AdminKeyCredential.FromSecret(config[AdminKeyCredential.ConfigurationKey]));

        services.AddRateLimiter(options =>
        {
            // Strict, per IP and per hour: issuing keys is the abuse surface. Rejections are immediate (no queue).
            options.AddPolicy<string>(ApiKeyEndpoints.KeyIssuePolicy, context =>
            {
                var perHour = context.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value.KeyIssuePerIpPerHour;
                var clientIp = context.RequestServices.GetRequiredService<ClientIpResolver>().Resolve(context);

                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
