namespace SchoolSearch.Reviews.Settings;

/// <summary>Configuration bound from the Reviews section (env: Reviews__...). SubjectPepper is a secret: never log it.</summary>
public sealed class ReviewsOptions
{
    public const string SectionName = "Reviews";

    /// <summary>
    /// Server-side pepper for author hashes (secret, env Reviews__SubjectPepper). Required in Production. Outside
    /// Production an ephemeral pepper is generated when this is empty.
    /// </summary>
    public string SubjectPepper { get; set; } = "";

    /// <summary>
    /// Terms version a review must accept (acceptedTermsVersion). "draft-1" marks a draft until a lawyer has reviewed it.
    /// The legal endpoints (E1b) read the same value.
    /// </summary>
    public string TermsVersion { get; set; } = "draft-1";

    /// <summary>Trust the Fly-Client-IP header. Set by the deployment only when a proxy is in front of the service.</summary>
    public bool TrustProxyClientIpHeader { get; set; }

    public ReviewsRateLimitingOptions RateLimiting { get; set; } = new();
}

public sealed class ReviewsRateLimitingOptions
{
    /// <summary>Anonymous requests per client IP per minute.</summary>
    public int PerIpPerMinute { get; set; } = 60;

    /// <summary>Notices (DSA art. 16) per client IP per hour.</summary>
    public int NoticesPerIpPerHour { get; set; } = 10;

    /// <summary>Authenticated writes (reviews, replies, deletes) per subject per hour.</summary>
    public int AuthWritesPerHour { get; set; } = 10;

    /// <summary>Process-wide concurrency bound beneath the per-client windows.</summary>
    public int ConcurrentRequests { get; set; } = 64;
}
