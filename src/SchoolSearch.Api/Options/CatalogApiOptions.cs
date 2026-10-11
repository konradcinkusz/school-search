namespace SchoolSearch.Api.Options;

/// <summary>Configuration bound from the SchoolSearch section (env: SchoolSearch__...).</summary>
public sealed class CatalogApiOptions
{
    public const string SectionName = "SchoolSearch";

    /// <summary>
    /// Trust the Fly-Client-IP header. Only true when a proxy is in front of the app, set by the
    /// deployment (flyio/api.fly.toml). With nothing in front, the header is client-supplied and
    /// trusting it would let every visitor choose their own bucket.
    /// </summary>
    public bool TrustProxyClientIpHeader { get; set; }

    public RateLimitingOptions RateLimiting { get; set; } = new();
}

public sealed class RateLimitingOptions
{
    /// <summary>Anonymous requests per client IP per minute.</summary>
    public int PerIpPerMinute { get; set; } = 60;

    /// <summary>Process-wide concurrency bound beneath the per-client window.</summary>
    public int ConcurrentRequests { get; set; } = 64;
}
