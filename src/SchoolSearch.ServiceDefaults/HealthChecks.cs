using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace SchoolSearch.ServiceDefaults;

/// <summary>
/// Health registration. Readiness (<c>/health</c>) reports every check and every optional
/// integration; liveness (<c>/alive</c>) reports only the <c>live</c>-tagged check.
/// Degraded integrations do not change the HTTP status (P8): only an Unhealthy check returns 503.
/// </summary>
internal static class HealthChecksSetup
{
    internal const string LiveTag = "live";
    internal const string ReadinessPath = "/health";
    internal const string LivenessPath = "/alive";

    internal static IServiceCollection AddDefaultHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), new[] { LiveTag });

        return services;
    }
}

/// <summary>Writes the JSON body for <c>/health</c>.</summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static Task WriteAsync(HttpContext context, HealthReport report, IntegrationStatusRegistry registry)
    {
        var integrations = registry.Snapshot();

        var anyDegraded = report.Status == HealthStatus.Degraded
            || integrations.Any(i => i.State == IntegrationState.Degraded);

        var unhealthy = report.Status == HealthStatus.Unhealthy;

        var status = unhealthy ? "unhealthy" : anyDegraded ? "degraded" : "ok";

        var serviceName = context.RequestServices.GetRequiredService<IHostEnvironment>().ApplicationName;

        var payload = new
        {
            status,
            service = serviceName,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Status.ToString().ToLowerInvariant()),
            integrations = integrations.Select(i => new
            {
                name = i.Name,
                state = i.State.ToString().ToLowerInvariant(),
                detail = i.Detail
            })
        };

        context.Response.StatusCode = unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
