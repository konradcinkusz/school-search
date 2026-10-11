using System.Net.Http;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Tests;

/// <summary>Builders shared by the Billing tests. Keep the signatures stable: other test files use them.</summary>
internal static class BillingFixtures
{
    public const string DefaultSlug = "szkola-testowa-1";
    public const string Owner = "owner-1";
    public const string Moderator = "moderator-1";

    public static SchoolClaim ClaimOf(string slug, string ownerHash, string status = ClaimStatuses.Approved) => new()
    {
        Id = Guid.NewGuid(),
        SchoolSlug = slug,
        OwnerHash = ownerHash,
        RoleDescription = "Dyrekcja",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        DecidedAt = status == ClaimStatuses.Pending ? null : DateTimeOffset.UtcNow.AddDays(-2),
    };

    public static Subscription SubscriptionOf(
        string slug,
        string ownerHash,
        string status = SubscriptionStatuses.Active,
        DateTimeOffset? periodEnd = null) => new()
    {
        Id = Guid.NewGuid(),
        SchoolSlug = slug,
        OwnerHash = ownerHash,
        PlanCode = ProductPlan,
        Status = status,
        CurrentPeriodEnd = periodEnd ?? DateTimeOffset.UtcNow.AddDays(30),
        CancelAtPeriodEnd = status == SubscriptionStatuses.Canceling,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    public static PremiumProfile ProfileOf(string slug, bool locked = false) => new()
    {
        SchoolSlug = slug,
        Headline = "Szkoła z klasami sportowymi",
        Description = "Zajęcia sportowe od drugiej klasy i język angielski w każdej klasie.",
        WebsiteUrl = "https://szkola.example.pl",
        IsLocked = locked,
        LockedReason = locked ? ProfileLockReason : null,
        LockedAt = locked ? DateTimeOffset.UtcNow.AddDays(-1) : null,
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    /// <summary>The plan code the test factory configures.</summary>
    public const string ProductPlan = "test-plan";

    private const string ProfileLockReason = "subscription_expired";

    /// <summary>JSON body for PUT /api/v1/schools/{slug}/premium.</summary>
    public static object ValidProfileBody(string headline = "Szkoła z klasami sportowymi") => new
    {
        headline,
        description = "Zajęcia sportowe od drugiej klasy i język angielski w każdej klasie.",
        websiteUrl = "https://szkola.example.pl",
    };

    /// <summary>
    /// Builds a request. A non-null <paramref name="sub"/> sends the test identity headers; null sends an anonymous request.
    /// </summary>
    public static HttpRequestMessage Request(
        HttpMethod method,
        string url,
        string? sub = null,
        bool verified = true,
        string? role = null,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (sub is not null)
        {
            request.Headers.Add(TestAuthHandler.SubHeader, sub);
            request.Headers.Add(TestAuthHandler.EmailVerifiedHeader, verified ? "true" : "false");
            if (role is not null)
            {
                request.Headers.Add(TestAuthHandler.RoleHeader, role);
            }
        }

        return request;
    }
}
