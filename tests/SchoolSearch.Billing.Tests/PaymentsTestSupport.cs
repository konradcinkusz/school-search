using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Payments;

namespace SchoolSearch.Billing.Tests;

/// <summary>Builders and request helpers for the Payments tests.</summary>
internal static class PaymentsTestSupport
{
    public const string WebhookPath = "/api/v1/billing/webhook";
    public const string SimulatePath = "/api/v1/billing/simulate";
    public const string Slug = "szkola-premium-platnosci";
    public const string OwnerSub = "owner-sub-payments";
    public const string OtherSub = "other-sub-payments";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A provider-shaped event in the GenericJsonEventMapper wire format. Null optional fields are omitted.</summary>
    public static Dictionary<string, object?> Event(
        string id,
        string type,
        DateTimeOffset occurredAt,
        Guid? subscriptionId = null,
        string? schoolSlug = null,
        string? ownerHash = null,
        string? planCode = null,
        DateTimeOffset? periodEnd = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["type"] = type,
            ["occurred_at"] = occurredAt,
        };

        if (subscriptionId is { } sid)
        {
            payload["subscription_id"] = sid;
        }

        if (schoolSlug is not null)
        {
            payload["school_slug"] = schoolSlug;
        }

        if (ownerHash is not null)
        {
            payload["owner_hash"] = ownerHash;
        }

        if (planCode is not null)
        {
            payload["plan_code"] = planCode;
        }

        if (periodEnd is { } pe)
        {
            payload["period_end"] = pe;
        }

        return payload;
    }

    public static Subscription NewSubscription(string ownerHash, string status, DateTimeOffset? periodEnd, bool cancelAtPeriodEnd = false, string planCode = "monthly") => new()
    {
        Id = Guid.NewGuid(),
        SchoolSlug = Slug,
        OwnerHash = ownerHash,
        PlanCode = planCode,
        Status = status,
        CurrentPeriodEnd = periodEnd,
        CancelAtPeriodEnd = cancelAtPeriodEnd,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static PremiumProfile NewProfile(bool isLocked = false, string? lockedReason = null) => new()
    {
        SchoolSlug = Slug,
        Headline = "Szkoła z płatnym profilem",
        Description = "Opis przygotowany przez dyrekcję szkoły.",
        WebsiteUrl = "https://szkola.example.test",
        IsLocked = isLocked,
        LockedReason = lockedReason,
        LockedAt = isLocked ? DateTimeOffset.UtcNow : null,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, object payload, string? secret = PaymentsTestFactory.Secret) =>
        PostRawWebhookAsync(client, JsonSerializer.Serialize(payload, Json), secret);

    /// <summary>Posts the exact bytes given. The signature, when a secret is given, is computed over those same bytes.</summary>
    public static async Task<HttpResponseMessage> PostRawWebhookAsync(HttpClient client, string raw, string? secret = PaymentsTestFactory.Secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(raw, Encoding.UTF8, "application/json"),
        };

        if (secret is not null)
        {
            request.Headers.Add("X-Signature", WebhookSignature.Sign(secret, raw));
        }

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> SimulateAsync(HttpClient client, Guid subscriptionId, string eventName, string? sub, bool verified = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SimulatePath)
        {
            Content = JsonContent.Create(new { subscriptionId, @event = eventName }, options: Json),
        };

        if (sub is not null)
        {
            request.Headers.Add(PaymentsTestAuthHandler.SubHeader, sub);
            request.Headers.Add(PaymentsTestAuthHandler.EmailVerifiedHeader, verified ? "true" : "false");
        }

        return await client.SendAsync(request);
    }

    public static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }

    public static async Task<bool> DuplicateFlagAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("duplicate").GetBoolean();
    }
}
