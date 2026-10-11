using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Reviews.Errors;
using SchoolSearch.Reviews.Hosting;
using SchoolSearch.Reviews.RateLimiting;
using SchoolSearch.Reviews.Security;
using SchoolSearch.Reviews.Settings;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Reviews.Extensions;

/// <summary>Wiring for the Reviews service. Called from Program.cs; keeps the composition root a manifest.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReviewsServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReviewsOptions>(configuration.GetSection(ReviewsOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ClientIpResolver>();
        services.AddReviewsRateLimiting();

        // Migrations run after Kestrel has started, so /health answers while schema work is in flight (P4).
        services.AddSingleton<MigrationCompletionSignal>();
        services.AddHostedService<MigrationHostedService>();

        return services;
    }

    /// <summary>
    /// Pipeline order: error shape (outermost), security headers, body limit, production storage guard,
    /// authentication, rate limiter, authorization. Authentication must precede the limiter, which partitions on the caller's subject.
    /// Routing is added by the host.
    /// </summary>
    public static WebApplication UseReviewsPipeline(this WebApplication app)
    {
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RequestBodyLimitMiddleware>();
        app.UseMiddleware<StorageGuardMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }
}
