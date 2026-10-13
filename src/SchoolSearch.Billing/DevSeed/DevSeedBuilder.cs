using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>The rows the dev seed writes. Lists only: the database writes happen in <see cref="DevSeeder"/>.</summary>
public sealed record DevSeedRows(
    IReadOnlyList<SchoolClaim> Claims,
    IReadOnlyList<Subscription> Subscriptions,
    IReadOnlyList<PremiumProfile> Profiles,
    IReadOnlyList<Lead> Leads);

/// <summary>
/// Builds the billing dev seed rows. Pure: no database and no clock except the <c>now</c> argument, so the states can be
/// tested without a host. Entities are written directly (not through the endpoints) because the seed needs history
/// (an approval with its decision time and moderator) and states the endpoints would not produce in one step.
/// </summary>
public static class DevSeedBuilder
{
    /// <param name="ownerHash">Owner key of the seeded owner: SubjectHasher.Hash(owner sub).</param>
    /// <param name="moderatorHash">Key of the deciding moderator, or null when no moderator subject was sent.</param>
    /// <param name="now">Seed time. All timestamps are relative to it.</param>
    public static DevSeedRows Build(string ownerHash, string? moderatorHash, DateTimeOffset now)
    {
        var approved = new SchoolClaim
        {
            Id = DevSeedContent.Ids.ApprovedClaim,
            SchoolSlug = DevSeedContent.OwnedSchool,
            OwnerHash = ownerHash,
            RoleDescription = "Dyrekcja (DEMO)",
            Status = ClaimStatuses.Approved,
            CreatedAt = now.AddDays(-10),
            DecidedAt = now.AddDays(-9),
            DecidedBy = moderatorHash,
            Notes = DevSeedContent.SourceNote,
        };

        var pending = new SchoolClaim
        {
            Id = DevSeedContent.Ids.PendingClaim,
            SchoolSlug = DevSeedContent.PendingSchool,
            OwnerHash = ownerHash,
            RoleDescription = "Sekretariat (DEMO)",
            Status = ClaimStatuses.Pending,
            CreatedAt = now.AddDays(-2),
        };

        // Active and not expired: entitlement holds for demo-sp-1 until the period end.
        var subscription = new Subscription
        {
            Id = DevSeedContent.Ids.Subscription,
            SchoolSlug = DevSeedContent.OwnedSchool,
            OwnerHash = ownerHash,
            PlanCode = DevSeedContent.PlanCode,
            Status = SubscriptionStatuses.Active,
            CurrentPeriodEnd = now.AddDays(30),
            CancelAtPeriodEnd = false,
            ProviderRef = DevSeedContent.ProviderRef,
            CreatedAt = now.AddDays(-9),
            UpdatedAt = now.AddDays(-9),
        };

        // Published: the row exists and is not locked. The public envelope labels it as sponsored content.
        var profile = new PremiumProfile
        {
            SchoolSlug = DevSeedContent.OwnedSchool,
            Headline = DevSeedContent.SponsoredHeadline,
            Description = DevSeedContent.SponsoredDescription,
            WebsiteUrl = DevSeedContent.SponsoredWebsite,
            IsLocked = false,
            UpdatedAt = now.AddDays(-1),
        };

        var leads = DevSeedContent.Enquiries
            .Select(fixture => new Lead
            {
                Id = fixture.Id,
                SchoolSlug = DevSeedContent.OwnedSchool,
                ParentEmail = fixture.ParentEmail,
                Message = fixture.Message,
                ConsentVersion = LeadConsent.Version,
                ConsentAt = now.AddDays(-fixture.DaysAgo),
                CreatedAt = now.AddDays(-fixture.DaysAgo),
                ExpiresAt = now.AddDays(-fixture.DaysAgo).AddDays(LeadRules.RetentionDays),
                Status = fixture.Status,
            })
            .ToList();

        return new DevSeedRows(
            Claims: new[] { approved, pending },
            Subscriptions: new[] { subscription },
            Profiles: new[] { profile },
            Leads: leads);
    }
}
