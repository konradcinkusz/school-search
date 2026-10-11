using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SchoolSearch.ServiceDefaults;

/// <summary>
/// Prints the same integration list that <c>/health</c> reports, once, at startup (P8).
/// Written through ILogger so it lands in the console in development and in the container log in production.
/// </summary>
internal sealed class StartupBannerService(
    IHostEnvironment environment,
    IntegrationStatusRegistry registry,
    ILogger<StartupBannerService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var banner = new StringBuilder();
        banner.AppendLine();
        banner.AppendLine($"  SchoolSearch :: {environment.ApplicationName} ({environment.EnvironmentName})");
        banner.AppendLine("  Optional integrations:");

        var snapshot = registry.Snapshot();
        if (snapshot.Count == 0)
        {
            banner.AppendLine("    (none registered)");
        }

        foreach (var integration in snapshot)
        {
            banner.AppendLine(
                $"    {integration.Name,-20} {integration.State.ToString().ToLowerInvariant(),-9} {integration.Detail}");
        }

        logger.LogInformation("{Banner}", banner.ToString());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
