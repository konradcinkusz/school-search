using Microsoft.Extensions.Hosting;
using SchoolSearch.Api.Hosting;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Demo;

/// <summary>
/// Seeds the synthetic demo gmina once the catalog schema is ready (see <see cref="MigrationCompletionSignal"/>).
/// Refuses to run when the environment is Production. Failures are logged server-side and never stop the host.
/// </summary>
public sealed class DemoSeedHostedService(
    IServiceScopeFactory scopes,
    MigrationCompletionSignal migration,
    IHostEnvironment environment,
    ILogger<DemoSeedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsProduction())
        {
            logger.LogWarning("SCHOOLSEARCH_DEMO_SEED=true is ignored in the Production environment; the demo seed does not run.");
            return;
        }

        try
        {
            var schemaReady = await migration.Completion.WaitAsync(stoppingToken);
            if (!schemaReady)
            {
                logger.LogWarning("Demo seed skipped: the catalog schema was not applied.");
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var report = await DemoSeed.ApplyAsync(db, stoppingToken);

            logger.LogInformation(
                "Demo seed finished: {RowsAdded} synthetic rows added (gmina {GminaTeryt}). Repeat runs add nothing.",
                report.RowsAdded,
                DemoSeed.GminaTeryt);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown while waiting for the schema or during the seed: nothing to report.
        }
        catch (Exception ex)
        {
            // Server-side log only: provider messages may echo connection details.
            logger.LogError(ex, "Demo seed failed");
        }
    }
}
