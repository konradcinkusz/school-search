using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Reviews.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Reviews.Hosting;

/// <summary>
/// Applies the reviews schema after Kestrel has started (P4). Relational providers use MigrateAsync; only the
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
        integrations.Register(ReviewsIntegrationNames.ReviewsDatabase, IntegrationState.Degraded, "migrating");

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            using var stopRegistration = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
            await started.Task;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();

            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(stoppingToken);
                integrations.Register(ReviewsIntegrationNames.ReviewsDatabase, IntegrationState.Ok, "migrations applied");
            }
            else
            {
                await db.Database.EnsureCreatedAsync(stoppingToken);
                integrations.Register(ReviewsIntegrationNames.ReviewsDatabase, IntegrationState.Ok, "in-memory store");
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
            logger.LogError(ex, "Reviews database migration failed");
            integrations.Register(ReviewsIntegrationNames.ReviewsDatabase, IntegrationState.Degraded, "migration failed; see server logs");
        }
        finally
        {
            signal.Complete(false);
        }
    }
}

/// <summary>Integration names as they appear in /health.</summary>
public static class ReviewsIntegrationNames
{
    public const string ReviewsDatabase = "reviewsdb";
    public const string Authservice = "authservice";
    public const string SubjectPepper = "reviews-pepper";
}
