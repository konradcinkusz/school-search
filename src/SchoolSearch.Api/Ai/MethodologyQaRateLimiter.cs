using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Per-client allowance for methodology answers that reach the model (cache hits are free). Fixed one-hour window per
/// client key; the key is the resolved client address, never a question value.
/// </summary>
public sealed class MethodologyQaRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public MethodologyQaRateLimiter(IOptions<MethodologyQaOptions> options)
    {
        var perHour = Math.Max(1, options.Value.AnswersPerIpPerHour);
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
