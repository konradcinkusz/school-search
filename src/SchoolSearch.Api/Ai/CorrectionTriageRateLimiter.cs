using System.Threading.RateLimiting;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Per-moderator allowance for triage calls that reach the model (cache hits are free). Fixed one-hour window keyed by
/// the admin subject, so one moderator cannot drain the shared budget and public clients never share this bucket.
/// </summary>
public sealed class CorrectionTriageRateLimiter : IDisposable
{
    /// <summary>Model calls one moderator may trigger per hour.</summary>
    public const int PerSubjectPerHour = 30;

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(subjectKey =>
        RateLimitPartition.GetFixedWindowLimiter(subjectKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PerSubjectPerHour,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        }));

    /// <summary>True when the subject may trigger one more model call in the current window.</summary>
    public bool TryAcquire(string subjectKey)
    {
        using var lease = _limiter.AttemptAcquire("triage:" + subjectKey);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
