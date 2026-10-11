using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Billing.Ai;

/// <summary>
/// Process-wide daily token counter for drafts (input plus output, all owners). Resets at UTC midnight. A call is allowed
/// while the day's spend is below the budget. In memory: a restart starts the day again (cost guard, not billing).
/// </summary>
public sealed class DraftTokenBudget(TimeProvider clock, IOptions<AiDraftOptions> options)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private long _spent;

    public bool IsExhausted()
    {
        lock (_gate)
        {
            RollToToday();
            return _spent >= options.Value.DailyTokenBudget;
        }
    }

    public void Record(long tokens)
    {
        if (tokens <= 0)
        {
            return;
        }

        lock (_gate)
        {
            RollToToday();
            _spent += tokens;
        }
    }

    private void RollToToday()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _spent = 0;
        }
    }
}

/// <summary>
/// Per-owner allowance for drafts that reach the model. Fixed one-hour window per owner. The key is the owner's subject
/// hash, never a school slug or the raw subject.
/// </summary>
public sealed class DraftRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public DraftRateLimiter(IOptions<AiDraftOptions> options)
    {
        var perHour = Math.Max(1, options.Value.DraftsPerSubjectPerHour);
        _limiter = PartitionedRateLimiter.Create<string, string>(ownerKey =>
            RateLimitPartition.GetFixedWindowLimiter(ownerKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perHour,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
    }

    /// <summary>True when the owner may start one more draft in the current window.</summary>
    public bool TryAcquire(string ownerKey)
    {
        using var lease = _limiter.AttemptAcquire(ownerKey);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
