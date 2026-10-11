using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Per-client allowance for search interpretations that reach the model (cache hits are free). Its own bucket, so
/// interpretation calls never spend the explain allowance. Fixed one-hour window per resolved client address.
/// </summary>
public sealed class SearchIntentRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public SearchIntentRateLimiter(IOptions<SearchIntentOptions> options)
    {
        var perHour = Math.Max(1, options.Value.QuestionsPerIpPerHour);
        _limiter = PartitionedRateLimiter.Create<string, string>(clientKey =>
            RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perHour,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));
    }

    /// <summary>True when the client may trigger one more model call in the current window.</summary>
    public bool TryAcquire(string clientKey)
    {
        using var lease = _limiter.AttemptAcquire(clientKey);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
