using Microsoft.Extensions.Options;

namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// Process-wide daily token counter for triage (input plus output). Resets at UTC midnight. A call is allowed while the
/// day's spend is below the budget. In memory: a restart starts the day again (cost guard, not billing).
/// </summary>
public sealed class ReviewTriageTokenBudget(TimeProvider clock, IOptions<AiTriageOptions> options)
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
/// Sliding one-minute window over model calls made by this process (on demand and from the worker together). Each
/// call that reaches the model takes one permit; pending reviews wait for the next permit.
/// </summary>
public sealed class ReviewTriageRateLimiter(TimeProvider clock, IOptions<AiTriageOptions> options)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _recent = new();

    /// <summary>True when one more model call may start now.</summary>
    public bool TryAcquire()
    {
        var limit = Math.Max(1, options.Value.TriagesPerMinute);
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            while (_recent.Count > 0 && now - _recent.Peek() >= Window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= limit)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
