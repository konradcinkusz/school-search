using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.ServiceDiscovery;

namespace SchoolSearch.ServiceDefaults;

/// <summary>
/// The shared kernel's entry points (P2). Plumbing only: telemetry, health, discovery,
/// resilience, OpenAPI, CORS and the database provider. No entity, DTO, seed data or user-facing text.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Call once, early, from <c>Program.cs</c>: OpenTelemetry (OTLP when
    /// OTEL_EXPORTER_OTLP_ENDPOINT is set), health checks, service discovery, the standard
    /// resilience handler on every HttpClient, OpenAPI, the integration registry and the startup banner.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();

        builder.Services.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.Services.AddOpenApi();

        builder.Services.GetOrAddRegistry();
        builder.Services.AddHostedService<StartupBannerService>();

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (readiness, JSON, lists every optional integration with its state),
    /// <c>/alive</c> (liveness, the <c>live</c>-tagged check only) and the OpenAPI document.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var registry = app.Services.GetRequiredService<IntegrationStatusRegistry>();

        app.MapOpenApi();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = (context, report) => HealthResponseWriter.WriteAsync(context, report, registry)
        });

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthChecksSetup.LiveTag)
        });

        return app;
    }
}
