namespace SchoolSearch.Billing.Settings;

/// <summary>
/// Configuration bound from the Billing section (env: Billing__...). SubjectPepper, WebhookSecret and any provider
/// credential are secrets: never log them, never return them.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Server-side pepper for subject hashes (secret, Billing__SubjectPepper). Required in Production.</summary>
    public string SubjectPepper { get; set; } = "";

    /// <summary>
    /// Mock mode (Billing__MockMode). Unset means true outside Production and false in Production. In mock mode checkout
    /// returns a synthetic URL and the simulate endpoint works; outside mock mode simulate answers 403.
    /// </summary>
    public bool? MockMode { get; set; }

    /// <summary>Webhook signing secret (secret, Billing__WebhookSecret). Empty means no signature can be verified.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Request header that carries the webhook signature (Billing__SignatureHeader). Confirm against real traffic.</summary>
    public string SignatureHeader { get; set; } = "X-Signature";

    /// <summary>Base URL of the synthetic mock checkout (Billing__CheckoutBaseUrl). Empty means a relative URL.</summary>
    public string CheckoutBaseUrl { get; set; } = "";

    /// <summary>Plan catalogue (Billing__Plans__0__Code, ...). Empty by default: the pricing page then says it is in preparation.</summary>
    public List<PlanOptions> Plans { get; set; } = [];

    /// <summary>Minutes between expiry passes of SubscriptionExpirationService.</summary>
    public int ExpirationIntervalMinutes { get; set; } = 15;

    /// <summary>Trust the Fly-Client-IP header. Set by the deployment only when a proxy is in front of the service.</summary>
    public bool TrustProxyClientIpHeader { get; set; }

    public BillingRateLimitingOptions RateLimiting { get; set; } = new();
}

/// <summary>One configured plan. The price is informational; entitlement never reads an amount (plans are server-side).</summary>
public sealed class PlanOptions
{
    /// <summary>Lower-case identifier, 1 to 50 characters, letters, digits and hyphens. Unique.</summary>
    public string Code { get; set; } = "";

    /// <summary>Display name, 1 to 120 characters.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gross price in PLN, or null when the price is not published yet. Never invented.</summary>
    public decimal? PriceGrossPln { get; set; }

    /// <summary>"month" or "year".</summary>
    public string Interval { get; set; } = "month";
}

public sealed class BillingRateLimitingOptions
{
    /// <summary>Anonymous requests per client IP per minute.</summary>
    public int PerIpPerMinute { get; set; } = 60;

    /// <summary>Parent enquiries per client IP per hour (used by the Leads policy).</summary>
    public int EnquiriesPerIpPerHour { get; set; } = 5;

    /// <summary>Authenticated writes (claims, checkout, cancel, resume, profile) per subject per hour.</summary>
    public int AuthWritesPerHour { get; set; } = 20;

    /// <summary>Process-wide concurrency bound beneath the per-client windows.</summary>
    public int ConcurrentRequests { get; set; } = 64;
}
