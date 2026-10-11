using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Catalog.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Api.Hosting;

/// <summary>
/// Applies the catalog schema after Kestrel has started (P4). Relational providers use
/// MigrateAsync; only the InMemory path uses EnsureCreated. Failure degrades the integration
/// in /health and does not stop the process, so the platform can still read the reason.
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
        integrations.Register(IntegrationNames.CatalogDatabase, "degraded", "migrating");
        integrations.Register(IntegrationNames.Scoring, "disabled", "Phase B: scoring engine not yet implemented");

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            using var stopRegistration = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
            await started.Task;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(stoppingToken);
                integrations.Register(IntegrationNames.CatalogDatabase, "ok", "migrations applied");
            }
            else
            {
                await db.Database.EnsureCreatedAsync(stoppingToken);
                integrations.Register(IntegrationNames.CatalogDatabase, "ok", "in-memory store");
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
            logger.LogError(ex, "Catalog database migration failed");
            integrations.Register(IntegrationNames.CatalogDatabase, "degraded", "migration failed; see server logs");
        }
        finally
        {
            signal.Complete(false);
        }
    }
}

/// <summary>Integration names as they appear in /health.</summary>
public static class IntegrationNames
{
    public const string CatalogDatabase = "catalogdb";
    public const string Scoring = "scoring";
}
