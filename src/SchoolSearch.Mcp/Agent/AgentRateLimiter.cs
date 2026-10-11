using System.Threading.RateLimiting;

namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// Per-client allowance for agent requests: a fixed one-minute window per client key. The key is the resolved client
/// address, never a school or message value. A rejected request never reaches the model.
/// </summary>
public sealed class AgentRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public AgentRateLimiter(AgentOptions options)
    {
        var perMinute = Math.Max(1, options.RequestsPerIpPerMinute);
        _limiter = PartitionedRateLimiter.Create<string, string>(clientKey =>
            RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    }

    /// <summary>True when the client may send one more agent request in the current window.</summary>
    public bool TryAcquire(string clientKey)
    {
        using var lease = _limiter.AttemptAcquire(clientKey);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
