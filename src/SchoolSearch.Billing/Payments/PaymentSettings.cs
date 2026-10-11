using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SchoolSearch.Billing.Payments;

/// <summary>
/// Payment settings read from the Billing section (Billing__WebhookSecret, Billing__SignatureHeader, Billing__MockMode and
/// Billing__Plans__n__Code / Interval). Only the keys the webhook path needs are read here. MockMode defaults to true
/// outside Production and to false in Production, as the Billing configuration contract states.
/// The webhook secret is a secret: it is never logged or returned.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Billing";
    public const string DefaultSignatureHeader = "X-Signature";

    private readonly IReadOnlyDictionary<string, string> _intervals;

    public PaymentSettings(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(SectionName);

        var secret = section["WebhookSecret"];
        WebhookSecret = string.IsNullOrWhiteSpace(secret) ? null : secret;

        var header = section["SignatureHeader"];
        SignatureHeader = string.IsNullOrWhiteSpace(header) ? DefaultSignatureHeader : header;

        IsMockMode = bool.TryParse(section["MockMode"], out var mock) ? mock : !environment.IsProduction();

        var intervals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plan in section.GetSection("Plans").GetChildren())
        {
            var code = plan["Code"];
            var interval = plan["Interval"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(code) && (interval is "month" or "year"))
            {
                intervals.TryAdd(code, interval);
            }
        }

        _intervals = intervals;
    }

    /// <summary>The HMAC key for webhooks. Null when not configured.</summary>
    public string? WebhookSecret { get; }

    /// <summary>Header that carries the hex HMAC-SHA256 of the raw body.</summary>
    public string SignatureHeader { get; }

    /// <summary>True in mock mode: checkout is synthetic, simulate is available, and unsigned webhooks are accepted only when no secret is set.</summary>
    public bool IsMockMode { get; }

    /// <summary>"month" or "year" for a configured plan code; null when the plan is not configured.</summary>
    public string? IntervalFor(string planCode) =>
        _intervals.TryGetValue(planCode, out var interval) ? interval : null;
}
