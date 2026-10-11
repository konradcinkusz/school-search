using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Deletes parent enquiries past their ExpiresAt (created + 90 days). Runs in the background, after a short delay so
/// migrations have a chance to finish first. A failed run is logged by error type only and retried on the next tick.
/// </summary>
public sealed class LeadPurgeService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<LeadOptions> options,
    ILogger<LeadPurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Deletes every enquiry whose ExpiresAt is at or before <paramref name="now"/>, in batches. Returns the number
    /// deleted. Batches keep each transaction small, and the loop works on both relational and InMemory providers.
    /// </summary>
    public static async Task<int> PurgeExpiredAsync(BillingDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Leads
                .Where(l => l.ExpiresAt <= now)
                .OrderBy(l => l.ExpiresAt)
                .Take(LeadRules.PurgeBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                break;
            }

            db.Leads.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            total += batch.Count;

            if (batch.Count < LeadRules.PurgeBatchSize)
            {
                break;
            }
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.PurgeIntervalMinutes, 1, 1440));
            using var timer = new PeriodicTimer(interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: nothing to report.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var purged = await PurgeExpiredAsync(db, clock.GetUtcNow(), ct);
            if (purged > 0)
            {
                logger.LogInformation("Purged {Count} expired parent enquiries.", purged);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the type is logged: provider messages can echo connection details or row data.
            logger.LogWarning("Parent enquiry purge failed ({ErrorType}); retrying on the next run.", ex.GetType().Name);
        }
    }
}
