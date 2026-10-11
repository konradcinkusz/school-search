using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Hosting;
using SchoolSearch.Billing.Plans;
using SchoolSearch.Billing.RateLimiting;
using SchoolSearch.Billing.Security;
using SchoolSearch.Billing.Settings;
using SchoolSearch.Billing.Subscriptions;

namespace SchoolSearch.Billing.Extensions;

/// <summary>Wiring for the Billing core. Called from Program.cs; keeps the composition root a manifest.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Billing core: options, mode, plan catalogue (fails fast when invalid), rate limits, migrations, entitlement,
    /// the expiry pass and the health reporters. The payment, lead and AI subfolders register their own services.
    /// </summary>
    public static IServiceCollection AddBillingServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));
        var options = configuration.GetSection(BillingOptions.SectionName).Get<BillingOptions>() ?? new BillingOptions();

        // Mock mode defaults to on outside Production and to off in Production (brief §1c, P8).
        services.AddSingleton(new BillingMode(options.MockMode ?? !environment.IsProduction()));

        // Built here, not lazily, so an invalid Billing:Plans entry stops the process at startup.
        services.AddSingleton(PlanCatalog.FromOptions(options.Plans));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ClientIpResolver>();
        services.AddBillingRateLimiting();

        // Migrations run after Kestrel has started, so /health answers while schema work is in flight (P4).
        services.AddSingleton<MigrationCompletionSignal>();
        services.AddHostedService<MigrationHostedService>();
        services.AddHostedService<BillingProviderIntegrationReporter>();

        services.AddScoped<EntitlementService>();
        services.TryAddSingleton<ICheckoutGateway, UnconfiguredCheckoutGateway>();
        services.AddSingleton<SubscriptionExpirationService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SubscriptionExpirationService>());

        return services;
    }

    /// <summary>
    /// Pipeline order: error shape (outermost), security headers, body limit, authentication, rate limiter,
    /// authorization. Authentication must precede the limiter, which partitions on the caller's subject.
    /// Routing is added by the host.
    /// </summary>
    public static WebApplication UseBillingPipeline(this WebApplication app)
    {
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RequestBodyLimitMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }
}

/// <summary>Routing for the Billing core endpoints. Subfolders map their own routes in Program.cs (WIRING markers).</summary>
public static class BillingEndpointRouting
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPlanEndpoints();
        app.MapClaimEndpoints();
        app.MapModerationEndpoints();
        app.MapPremiumEndpoints();
        app.MapSubscriptionEndpoints();
        return app;
    }
}
