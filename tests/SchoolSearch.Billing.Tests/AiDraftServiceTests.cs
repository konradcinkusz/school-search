using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Ai;
using Xunit;

namespace SchoolSearch.Billing.Tests;

/// <summary>The profile-copy assistant: a draft is returned to the owner only, and every gate holds.</summary>
public sealed class AiDraftServiceTests
{
    [Fact]
    public async Task Draft_is_returned_to_the_owner_and_nothing_is_saved()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Drafted, outcome.Status);
        Assert.Equal("Szkoła z klasami sportowymi", outcome.Draft!.Headline);
        Assert.Equal(1, client.CallCount);
        Assert.Empty(await db.PremiumProfiles.ToListAsync());
    }

    [Fact]
    public async Task Owner_without_an_approved_claim_gets_no_draft_and_no_model_call()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash, claimStatus: "pending");
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.NotEntitled, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Approved_claim_without_subscription_gets_no_draft()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash, subscriptionStatus: null);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.NotEntitled, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Expired_subscription_gets_no_draft()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(
            db,
            AiDraftTestSupport.OwnerHash,
            subscriptionStatus: "expired",
            periodEnd: AiDraftTestSupport.Start.AddDays(-1));
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.NotEntitled, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Canceling_subscription_before_period_end_still_gets_a_draft()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash, subscriptionStatus: "canceling");
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Drafted, outcome.Status);
    }

    [Fact]
    public async Task Another_owners_claim_does_not_open_the_draft()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OtherOwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.NotEntitled, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_read_or_sent()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start), available: false);

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Unavailable, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Empty_facts_are_refused_before_the_model()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, new ProfileDraftRequest(Array.Empty<string>()), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.InvalidInput, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Banned_claim_in_the_facts_is_refused_before_the_model()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(
            AiDraftTestSupport.Slug,
            AiDraftTestSupport.OwnerHash,
            new ProfileDraftRequest(new[] { "Najlepsza szkola w gminie" }),
            CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.ClaimNotAllowed, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Banned_claim_in_one_draft_is_retried_once_and_a_clean_retry_is_returned()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient
        {
            Respond = call => call == 1
                ? new DraftCompletion("{\"headline\":\"Najlepsza szkola\",\"description\":\"Szkoła prowadzi zajęcia.\"}", 120, 30)
                : new DraftCompletion(AiDraftTestSupport.ValidJson, 120, 30),
        };
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Drafted, outcome.Status);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task Two_bad_drafts_are_rejected_and_nothing_is_shown()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient
        {
            Respond = _ => new DraftCompletion("{\"headline\":\"Gwarantujemy wyniki\",\"description\":\"Szkoła.\"}", 120, 30),
        };
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Rejected, outcome.Status);
        Assert.Null(outcome.Draft);
        Assert.Equal(ProfileDraftService.MaxAttempts, client.CallCount);
    }

    [Fact]
    public async Task A_number_the_facts_do_not_contain_is_rejected()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient
        {
            Respond = _ => new DraftCompletion("{\"headline\":\"Szkoła od 2025 roku\",\"description\":\"Szkoła prowadzi zajęcia.\"}", 120, 30),
        };
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Rejected, outcome.Status);
    }

    [Fact]
    public async Task Refusal_or_cut_off_answer_is_rejected_after_one_retry()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient { Respond = _ => new DraftCompletion(string.Empty, 100, 1500) };
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Rejected, outcome.Status);
        Assert.Equal(ProfileDraftService.MaxAttempts, client.CallCount);
    }

    [Fact]
    public async Task Model_failure_is_unavailable_and_the_message_hides_the_exception_text()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient { ThrowOnCall = new HttpRequestException("secret-key-echo") };
        var service = AiDraftTestSupport.CreateService(db, client, new DraftTestClock(AiDraftTestSupport.Start));

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Unavailable, outcome.Status);
        Assert.DoesNotContain("secret-key-echo", outcome.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausted_daily_budget_makes_no_call()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(
            db,
            client,
            new DraftTestClock(AiDraftTestSupport.Start),
            new AiDraftOptions { DailyTokenBudget = 0 });

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.BudgetExhausted, outcome.Status);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Budget_spent_by_the_first_attempt_stops_the_retry()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        var client = new FakeDraftClient
        {
            Respond = _ => new DraftCompletion("{\"headline\":\"Najlepsza szkola\",\"description\":\"Szkoła.\"}", 120, 30),
        };
        var service = AiDraftTestSupport.CreateService(
            db,
            client,
            new DraftTestClock(AiDraftTestSupport.Start),
            new AiDraftOptions { DailyTokenBudget = 150 });

        var outcome = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.BudgetExhausted, outcome.Status);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Draft_allowance_is_per_owner()
    {
        using var db = AiDraftTestSupport.CreateDb();
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OwnerHash);
        await AiDraftTestSupport.SeedEntitlementAsync(db, AiDraftTestSupport.OtherOwnerHash);
        var client = new FakeDraftClient();
        var service = AiDraftTestSupport.CreateService(
            db,
            client,
            new DraftTestClock(AiDraftTestSupport.Start),
            new AiDraftOptions { DraftsPerSubjectPerHour = 1 });

        var first = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);
        var second = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);
        var other = await service.DraftAsync(AiDraftTestSupport.Slug, AiDraftTestSupport.OtherOwnerHash, AiDraftTestSupport.ValidRequest(), CancellationToken.None);

        Assert.Equal(ProfileDraftStatus.Drafted, first.Status);
        Assert.Equal(ProfileDraftStatus.RateLimited, second.Status);
        Assert.Equal(ProfileDraftStatus.Drafted, other.Status);
        Assert.Equal(2, client.CallCount);
    }
}
