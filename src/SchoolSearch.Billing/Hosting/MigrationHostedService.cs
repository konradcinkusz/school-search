using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Billing.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Hosting;

/// <summary>
/// Applies the billing schema after Kestrel has started (P4). Relational providers use MigrateAsync; only the
/// InMemory path uses EnsureCreated. Failure degrades the integration in /health and does not stop the process.
/// </summary>
public sealed class MigrationHostedService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    IntegrationStatusRegistry integrations,
    MigrationCompletionSignal signal,
    ILogger<MigrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        integrations.Register(BillingIntegrationNames.BillingDatabase, IntegrationState.Degraded, "migrating");

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            using var stopRegistration = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
            await started.Task;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(stoppingToken);
                integrations.Register(BillingIntegrationNames.BillingDatabase, IntegrationState.Ok, "migrations applied");
            }
            else
            {
                await db.Database.EnsureCreatedAsync(stoppingToken);
                integrations.Register(BillingIntegrationNames.BillingDatabase, IntegrationState.Ok, "in-memory store");
            }

            signal.Complete(true);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown before the app started or during migration: nothing to report.
        }
        catch (Exception ex)
        {
            // Logged server-side only: provider messages may echo connection details.
            logger.LogError(ex, "Billing database migration failed");
            integrations.Register(BillingIntegrationNames.BillingDatabase, IntegrationState.Degraded, "migration failed; see server logs");
        }
        finally
        {
            signal.Complete(false);
        }
    }
}

/// <summary>Integration names as they appear in /health and the startup banner.</summary>
public static class BillingIntegrationNames
{
    public const string BillingDatabase = "billingdb";
    public const string Authservice = "authservice";
    public const string SubjectPepper = "billing-pepper";

    /// <summary>The payment provider webhook. Disabled while in mock mode or without a webhook secret (P8).</summary>
    public const string BillingProvider = "billing-provider";
}
