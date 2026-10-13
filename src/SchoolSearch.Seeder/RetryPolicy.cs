using System;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolSearch.Seeder;

/// <summary>
/// Exponential backoff with a cap. Attempts are numbered from 0. A server hint (Retry-After) replaces the computed
/// delay, but is itself capped at <see cref="MaxServerHint"/> so a hostile or broken header cannot stall the run.
/// </summary>
public sealed record RetryPolicy(TimeSpan InitialDelay, TimeSpan MaxDelay, int MaxAttempts)
{
    public static readonly TimeSpan MaxServerHint = TimeSpan.FromMinutes(2);

    /// <summary>Per-call policy for authservice, reviews and billing calls (register, login, role, dev seed).</summary>
    public static RetryPolicy Requests { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), 6);

    /// <summary>Readiness polling. Bounded by the time budget, not by an attempt count.</summary>
    public static RetryPolicy Readiness { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), int.MaxValue);

    /// <summary>The delay before the next attempt. Attempt 0 waits <see cref="InitialDelay"/>, then the wait doubles up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan DelayFor(int attempt, TimeSpan? serverHint = null)
    {
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt numbers start at 0.");
        }

        if (serverHint is { } hint && hint > TimeSpan.Zero)
        {
            return hint < MaxServerHint ? hint : MaxServerHint;
        }

        double factor = Math.Pow(2, Math.Min(attempt, 30));
        double milliseconds = Math.Min(InitialDelay.TotalMilliseconds * factor, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}

/// <summary>Polls a probe until it succeeds or the time budget runs out. Time comes in as injected functions so tests can run on a virtual clock.</summary>
public static class ReadinessWaiter
{
    /// <returns>True when the probe succeeded; false when the budget was spent first.</returns>
    public static async Task<bool> WaitAsync(
        Func<CancellationToken, Task<bool>> probe,
        RetryPolicy policy,
        TimeSpan timeout,
        Func<TimeSpan> elapsed,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(elapsed);
        ArgumentNullException.ThrowIfNull(delay);

        for (int attempt = 0; ; attempt++)
        {
            if (await probe(cancellationToken))
            {
                return true;
            }

            TimeSpan remaining = timeout - elapsed();
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            TimeSpan wait = policy.DelayFor(attempt);
            if (wait > remaining)
            {
                wait = remaining;
            }

            await delay(wait, cancellationToken);
        }
    }
}
