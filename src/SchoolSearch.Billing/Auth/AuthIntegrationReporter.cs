using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Hosting;
using SchoolSearch.Billing.Settings;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Auth;

/// <summary>Writes the authservice and subject-pepper states into the integration list shown by /health (P8).</summary>
public sealed class AuthIntegrationReporter(
    IntegrationStatusRegistry registry,
    IOptions<AuthServiceOptions> auth,
    SubjectHasher hasher) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = auth.Value.IsConfigured;
        registry.Register(
            BillingIntegrationNames.Authservice,
            configured ? IntegrationState.Ok : IntegrationState.Disabled,
            configured
                ? "tokens validated against authservice discovery and JWKS"
                : "Auth:Authority, Auth:Issuer and Auth:Audience are required; auth-gated endpoints answer 503");

        var pepperDetail = hasher.IsEphemeral
            ? "ephemeral pepper (non-production): author hashes change on restart"
            : hasher.IsConfigured
                ? "subject pepper configured"
                : "Billing:SubjectPepper not set; claim, checkout and profile writes answer 503";

        registry.Register(
            BillingIntegrationNames.SubjectPepper,
            hasher.IsConfigured && !hasher.IsEphemeral ? IntegrationState.Ok : IntegrationState.Degraded,
            pepperDetail);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
