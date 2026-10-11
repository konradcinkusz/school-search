using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Ai;
using SchoolSearch.ServiceDefaults;
using Xunit;

namespace SchoolSearch.Reviews.Tests;

/// <summary>The triage cost guards: daily token budget and the per-minute window.</summary>
public sealed class AiTriageBudgetTests
{
    [Fact]
    public void Budget_is_spent_and_resets_at_utc_midnight()
    {
        var clock = new ManualClock(AiTriageTestSupport.Start);
        var budget = new ReviewTriageTokenBudget(clock, Options.Create(new AiTriageOptions { DailyTokenBudget = 100 }));

        Assert.False(budget.IsExhausted());
        budget.Record(100);
        Assert.True(budget.IsExhausted());

        clock.Advance(TimeSpan.FromDays(1));

        Assert.False(budget.IsExhausted());
    }

    [Fact]
    public void Rate_limiter_allows_the_configured_count_per_window_only()
    {
        var clock = new ManualClock(AiTriageTestSupport.Start);
        var limiter = new ReviewTriageRateLimiter(clock, Options.Create(new AiTriageOptions { TriagesPerMinute = 2 }));

        Assert.True(limiter.TryAcquire());
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(limiter.TryAcquire());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire());
    }

    [Fact]
    public void Announce_reports_ok_when_configured_and_disabled_without_a_key()
    {
        var registry = new IntegrationStatusRegistry();

        ReviewTriageService.Announce(registry, new AiTriageAvailability(true, "configured"));
        var configured = Assert.Single(registry.Snapshot(), e => e.Name == "anthropic");
        Assert.Equal(IntegrationState.Ok, configured.State);

        ReviewTriageService.Announce(registry, new AiTriageAvailability(false, "not set"));
        var disabled = Assert.Single(registry.Snapshot(), e => e.Name == "anthropic");
        Assert.Equal(IntegrationState.Disabled, disabled.State);
    }
}
