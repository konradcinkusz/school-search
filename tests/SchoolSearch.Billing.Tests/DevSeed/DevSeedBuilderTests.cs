using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.DevSeed;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.Tests.DevSeed;

/// <summary>The seeded states, built without a host. Endpoint tests cover the same states through the real service.</summary>
public sealed class DevSeedBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_creates_two_claims_one_subscription_one_profile_and_four_enquiries()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        var claimCount = rows.Claims.Count;
        var leadCount = rows.Leads.Count;
        Assert.Equal(2, claimCount);
        Assert.Single(rows.Subscriptions);
        Assert.Single(rows.Profiles);
        Assert.Equal(4, leadCount);
    }

    [Fact]
    public void The_owner_holds_an_approved_claim_on_demo_sp_1_decided_by_the_moderator()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        var approved = Assert.Single(rows.Claims, c => c.SchoolSlug == DevSeedContent.OwnedSchool);
        Assert.Equal("owner-hash", approved.OwnerHash);
        Assert.Equal(ClaimStatuses.Approved, approved.Status);
        Assert.Equal("moderator-hash", approved.DecidedBy);
        Assert.NotNull(approved.DecidedAt);
        Assert.True(approved.DecidedAt > approved.CreatedAt);
    }

    [Fact]
    public void The_owner_holds_a_pending_claim_on_demo_sp_2_with_no_decision()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        var pending = Assert.Single(rows.Claims, c => c.SchoolSlug == DevSeedContent.PendingSchool);
        Assert.Equal("owner-hash", pending.OwnerHash);
        Assert.Equal(ClaimStatuses.Pending, pending.Status);
        Assert.Null(pending.DecidedAt);
        Assert.Null(pending.DecidedBy);
    }

    [Fact]
    public void The_subscription_is_active_mock_and_runs_past_the_seed_time()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        var subscription = Assert.Single(rows.Subscriptions);
        Assert.Equal(DevSeedContent.OwnedSchool, subscription.SchoolSlug);
        Assert.Equal("owner-hash", subscription.OwnerHash);
        Assert.Equal(SubscriptionStatuses.Active, subscription.Status);
        Assert.Equal(DevSeedContent.ProviderRef, subscription.ProviderRef);
        Assert.True(subscription.CurrentPeriodEnd > Now);
        Assert.False(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void The_sponsored_profile_is_published_unlocked_and_labelled_as_demo_sponsored_content()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        var profile = Assert.Single(rows.Profiles);
        Assert.Equal(DevSeedContent.OwnedSchool, profile.SchoolSlug);
        Assert.False(profile.IsLocked);
        Assert.Contains("DEMO", profile.Headline, StringComparison.Ordinal);
        // ASCII stem: the Polish "Treść sponsorowana" is JSON-escaped on the wire, so the stem is what is asserted.
        Assert.Contains("sponsorowan", profile.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sponsorowan", profile.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(profile.WebsiteUrl?.StartsWith("https://", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void The_sponsored_text_passes_the_claim_linter_so_it_would_be_accepted_by_the_owner_endpoint()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);
        var profile = Assert.Single(rows.Profiles);

        Assert.Null(ClaimLinter.Check(profile.Headline));
        Assert.Null(ClaimLinter.Check(profile.Description));
    }

    [Fact]
    public void The_enquiries_go_to_demo_sp_1_use_reserved_addresses_and_keep_one_closed()
    {
        var rows = DevSeedBuilder.Build("owner-hash", "moderator-hash", Now);

        Assert.All(rows.Leads, lead => Assert.Equal(DevSeedContent.OwnedSchool, lead.SchoolSlug));
        Assert.All(rows.Leads, lead => Assert.EndsWith("@example.test", lead.ParentEmail, StringComparison.Ordinal));
        Assert.All(rows.Leads, lead => Assert.Equal(LeadConsent.Version, lead.ConsentVersion));
        Assert.All(rows.Leads, lead => Assert.True(lead.ExpiresAt > Now));
        Assert.All(rows.Leads, lead => Assert.Equal(lead.CreatedAt.AddDays(LeadRules.RetentionDays), lead.ExpiresAt));

        Assert.Single(rows.Leads, lead => lead.Status == LeadStatuses.Read);
        var newCount = rows.Leads.Count(lead => lead.Status == LeadStatuses.New);
        Assert.Equal(3, newCount);
    }

    [Fact]
    public void Ids_are_fixed_so_a_repeated_build_produces_the_same_rows()
    {
        var first = DevSeedBuilder.Build("owner-hash", null, Now);
        var second = DevSeedBuilder.Build("owner-hash", null, Now.AddDays(1));

        Assert.Equal(first.Claims.Select(c => c.Id), second.Claims.Select(c => c.Id));
        Assert.Equal(first.Leads.Select(l => l.Id), second.Leads.Select(l => l.Id));
        Assert.Equal(first.Subscriptions.Single().Id, second.Subscriptions.Single().Id);
    }

    [Fact]
    public void Without_a_moderator_subject_the_approval_has_no_decider_on_record()
    {
        var rows = DevSeedBuilder.Build("owner-hash", null, Now);

        var approved = Assert.Single(rows.Claims, c => c.Status == ClaimStatuses.Approved);
        Assert.Null(approved.DecidedBy);
    }
}
