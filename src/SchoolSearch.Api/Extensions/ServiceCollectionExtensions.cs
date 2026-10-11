using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Hosting;
using SchoolSearch.Api.Options;
using SchoolSearch.Api.RateLimiting;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Api.Extensions;

/// <summary>Wiring for the catalog Api. Called from Program.cs; keeps the composition root a manifest.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogApiOptions>(configuration.GetSection(CatalogApiOptions.SectionName));
        services.AddSingleton<ClientIpResolver>();
        services.AddCatalogRateLimiting();

        // Migrations run after Kestrel has started, so /health answers while schema work is in flight (P4).
        services.AddSingleton<MigrationCompletionSignal>();
        services.AddHostedService<MigrationHostedService>();

        return services;
    }

    /// <summary>Pipeline order: error shape (outermost), CORS, rate limiter. Routing is added by the host.</summary>
    public static WebApplication UseCatalogApi(this WebApplication app)
    {
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseCors(CorsPolicies.Frontend);
        app.UseRateLimiter();
        return app;
    }
}
