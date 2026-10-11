using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// Triages pending reviews that have no ModerationTriage row yet, in small batches, within the per-minute allowance.
/// Idle (no polling, no model calls) when AI is unavailable. A failed pass waits before it retries, so a schema that is
/// not ready yet or a provider outage does not turn into a loop.
/// </summary>
public sealed class ReviewTriageWorker(
    IServiceScopeFactory scopes,
    AiTriageAvailability availability,
    IntegrationStatusRegistry integrations,
    IOptions<AiTriageOptions> options,
    ILogger<ReviewTriageWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RateLimitedDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ReviewTriageService.Announce(integrations, availability);
        if (!availability.IsAvailable)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await TriageBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Usually the schema is not ready yet. Logged by exception type only.
                logger.LogWarning("Review triage pass failed with {ExceptionType}; retrying later", ex.GetType().Name);
                delay = FailureBackoff;
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<TimeSpan> TriageBatchAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var batchSize = Math.Max(1, settings.BatchSize);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ReviewTriageService>();

        var pendingIds = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewTriageService.PendingStatus
                        && !db.ModerationTriages.Any(t => t.ReviewId == r.Id))
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pendingIds.Count == 0)
        {
            return IdleDelay(settings);
        }

        foreach (var reviewId in pendingIds)
        {
            var outcome = await service.TriageAsync(reviewId, ct);
            switch (outcome.Status)
            {
                case ReviewTriageStatus.RateLimited:
                    return RateLimitedDelay;
                case ReviewTriageStatus.BudgetExhausted:
                case ReviewTriageStatus.Unavailable:
                    return IdleDelay(settings);
                case ReviewTriageStatus.Failed:
                    return FailureBackoff;
            }
        }

        return BusyDelay;
    }

    private static TimeSpan IdleDelay(AiTriageOptions settings) =>
        TimeSpan.FromSeconds(Math.Max(1, settings.IdlePollSeconds));
}
