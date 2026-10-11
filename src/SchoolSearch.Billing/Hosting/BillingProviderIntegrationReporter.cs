using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Settings;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Hosting;

/// <summary>
/// Reports the payment provider integration in /health (P8). Disabled while mock mode is on or no webhook secret is set:
/// webhooks are then refused (production) or accepted unsigned (mock only). Writes no state, so it can run any time.
/// </summary>
public sealed class BillingProviderIntegrationReporter(
    IntegrationStatusRegistry registry,
    BillingMode mode,
    IOptions<BillingOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var secretSet = !string.IsNullOrWhiteSpace(options.Value.WebhookSecret);

        if (mode.IsMock)
        {
            registry.Register(BillingIntegrationNames.BillingProvider, IntegrationState.Disabled, "mock mode: no provider credentials needed");
        }
        else if (!secretSet)
        {
            registry.Register(BillingIntegrationNames.BillingProvider, IntegrationState.Disabled, "Billing:WebhookSecret not set; webhooks are refused");
        }
        else
        {
            registry.Register(BillingIntegrationNames.BillingProvider, IntegrationState.Ok, "webhook signatures verified");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
