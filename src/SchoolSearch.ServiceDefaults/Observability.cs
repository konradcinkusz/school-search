using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SchoolSearch.ServiceDefaults;

/// <summary>
/// Telemetry (P15): traces, metrics and logs over OTLP. Health-probe requests are filtered
/// out of traces so probe noise does not dominate. No vendor-specific exporter is wired here.
/// </summary>
internal static class Observability
{
    internal static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = static context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation());

        // Export only when an OTLP endpoint is configured (Aspire injects one in development).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    private static bool IsProbe(PathString path) =>
        path.StartsWithSegments(HealthChecksSetup.ReadinessPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(HealthChecksSetup.LivenessPath, StringComparison.OrdinalIgnoreCase);
}
