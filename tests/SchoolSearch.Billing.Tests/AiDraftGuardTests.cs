using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Ai;
using Xunit;

namespace SchoolSearch.Billing.Tests;

/// <summary>The draft cost guard, the prompt contract and the availability wording.</summary>
public sealed class AiDraftGuardTests
{
    [Fact]
    public void Budget_is_spent_and_resets_at_utc_midnight()
    {
        var clock = new DraftTestClock(AiDraftTestSupport.Start);
        var budget = new DraftTokenBudget(clock, Options.Create(new AiDraftOptions { DailyTokenBudget = 100 }));

        Assert.False(budget.IsExhausted());
        budget.Record(100);
        Assert.True(budget.IsExhausted());

        clock.Advance(TimeSpan.FromDays(1));

        Assert.False(budget.IsExhausted());
    }

    [Fact]
    public void Prompt_rules_forbid_invented_facts_claims_and_rankings()
    {
        var prompt = DraftPromptBuilder.Build(AiDraftTestSupport.Facts);

        Assert.Contains("Use only the facts given", prompt.System, StringComparison.Ordinal);
        Assert.Contains("superlatives or rankings", prompt.System, StringComparison.Ordinal);
        Assert.Contains("Do not compare the school", prompt.System, StringComparison.Ordinal);
        Assert.Contains("guarantee", prompt.System, StringComparison.Ordinal);
        Assert.Contains("verification", prompt.System, StringComparison.Ordinal);
        Assert.Contains("Do not write any number", prompt.System, StringComparison.Ordinal);
        Assert.Contains("\"headline\"", prompt.System, StringComparison.Ordinal);
    }

    [Fact]
    public void Facts_are_escaped_and_the_prompt_carries_no_school_or_owner_identifier()
    {
        var prompt = DraftPromptBuilder.Build(new[] { "<b>Boisko</b> szkolne" });

        Assert.Contains("<untrusted source=\"facts\">", prompt.User, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Boisko&lt;/b&gt;", prompt.User, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", prompt.User, StringComparison.Ordinal);
        Assert.DoesNotContain(AiDraftTestSupport.Slug, prompt.User + prompt.System, StringComparison.Ordinal);
        Assert.DoesNotContain(AiDraftTestSupport.OwnerHash, prompt.User + prompt.System, StringComparison.Ordinal);
    }

    [Fact]
    public void Digit_grounding_accepts_only_numbers_the_facts_contain()
    {
        Assert.True(DraftChecks.DigitsGrounded("Klasa od 4 roku", "Klasy sportowe od 4 klasy"));
        Assert.True(DraftChecks.DigitsGrounded("Tekst bez liczb", "Klasy sportowe"));
        Assert.False(DraftChecks.DigitsGrounded("Od 2025 roku", "Klasy sportowe od 4 klasy"));
        Assert.False(DraftChecks.DigitsGrounded("Liczba 45", "Liczba 4"));
    }
}
