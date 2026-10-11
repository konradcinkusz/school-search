using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Per-client allowance for explanations that reach the model (cache hits are free). Fixed one-hour window per client
/// key; the key is the resolved client address, never a school or query value.
/// </summary>
public sealed class ExplainRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public ExplainRateLimiter(IOptions<AiOptions> options)
    {
        var perHour = Math.Max(1, options.Value.ExplainsPerIpPerHour);
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
