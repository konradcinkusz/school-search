using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Hosting;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>
/// Background expiry pass against the database, not dependent on provider "ended" events (brief §1e). A subscription whose
/// period has ended becomes expired and its premium profile is locked. Nothing is deleted. Idempotent: running it twice is a no-op.
/// Waits for the schema (P4) and one interval before its first pass.
/// </summary>
public sealed class SubscriptionExpirationService(
    IServiceScopeFactory scopes,
    MigrationCompletionSignal migration,
    IOptions<BillingOptions> options,
    TimeProvider time,
    ILogger<SubscriptionExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await migration.Completion.WaitAsync(stoppingToken))
            {
                logger.LogWarning("Subscription expiry pass not started: the billing schema is not ready");
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.ExpirationIntervalMinutes));
            while (!stoppingToken.IsCancellationRequested)
            {
                // The first pass comes one interval after start, so a restart never races the schema or a fresh deploy.
                await Task.Delay(interval, stoppingToken);

                try
                {
                    var expired = await RunOnceAsync(stoppingToken);
                    if (expired > 0)
                    {
                        logger.LogInformation("Subscription expiry pass expired {Count} subscription(s)", expired);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Subscription expiry pass failed; retrying on the next interval");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: nothing to report.
        }
    }

    /// <summary>One expiry pass. Returns the number of subscriptions that moved to expired.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var now = time.GetUtcNow();

        var due = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Canceling)
                        && s.CurrentPeriodEnd <= now)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return 0;
        }

        var slugs = due.Select(s => s.SchoolSlug).ToList();
        var profiles = await db.PremiumProfiles
            .Where(p => slugs.Contains(p.SchoolSlug))
            .ToListAsync(ct);

        foreach (var subscription in due)
        {
            subscription.Status = SubscriptionStatuses.Expired;
            subscription.UpdatedAt = now;
        }

        foreach (var profile in profiles.Where(p => !p.IsLocked))
        {
            ProfileLocks.Lock(profile, ProfileLocks.SubscriptionExpiredReason, now);
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
