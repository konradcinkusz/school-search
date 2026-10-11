using SchoolSearch.Reviews.Dsa;

namespace SchoolSearch.Reviews.Tests;

public sealed class DsaValidationTests
{
    private const string Explanation = "Opinia zawiera dane osobowe nauczyciela wymienione z nazwiska.";

    [Fact]
    public void Notice_explanation_must_be_long_enough()
    {
        var result = DsaValidation.ValidateNotice(new DsaNoticeRequest
        {
            ContentReference = "https://example.test/x",
            Explanation = "za krótko",
            Category = "other",
            GoodFaithConfirmed = true,
        });

        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Notice_strips_control_characters_but_keeps_line_breaks()
    {
        var result = DsaValidation.ValidateNotice(new DsaNoticeRequest
        {
            ContentReference = "https://example.test/x",
            Explanation = Explanation + "\u0001\u0007\n" + "Drugi wiersz wyjaśnienia.",
            Category = "other",
            GoodFaithConfirmed = true,
        });

        var value = Assert.IsType<NoticeInput>(result.Value);
        Assert.DoesNotContain('\u0001', value.Explanation);
        Assert.Contains('\n', value.Explanation);
    }

    [Fact]
    public void Notice_without_reference_or_review_is_refused()
    {
        var result = DsaValidation.ValidateNotice(new DsaNoticeRequest
        {
            Explanation = Explanation,
            Category = "other",
            GoodFaithConfirmed = true,
        });

        Assert.Null(result.Value);
    }

    [Fact]
    public void Notice_with_review_only_gets_a_review_reference()
    {
        var reviewId = Guid.NewGuid();
        var result = DsaValidation.ValidateNotice(new DsaNoticeRequest
        {
            ReviewId = reviewId,
            Explanation = Explanation,
            Category = "other",
            GoodFaithConfirmed = true,
        });

        var value = Assert.IsType<NoticeInput>(result.Value);
        Assert.Equal($"review:{reviewId}", value.ContentReference);
    }

    [Fact]
    public void Notice_submitter_email_must_have_an_address_shape()
    {
        var result = DsaValidation.ValidateNotice(new DsaNoticeRequest
        {
            ContentReference = "https://example.test/x",
            Explanation = Explanation,
            Category = "other",
            SubmitterEmail = "nie-adres",
            GoodFaithConfirmed = true,
        });

        Assert.Null(result.Value);
    }

    [Fact]
    public void Decision_action_must_belong_to_the_kind_of_content()
    {
        var body = new DsaDecisionRequest { Action = "keep", LegalOrTermsGround = "Art. 17 DSA", Facts = "Sprawdzono treść opinii." };

        Assert.Null(DsaValidation.ValidateDecision(body, DsaValidation.ReviewActions).Value);
        Assert.NotNull(DsaValidation.ValidateDecision(body, DsaValidation.NoticeActions).Value);
    }

    [Fact]
    public void Complaint_outcome_must_be_upheld_or_reversed()
    {
        var result = DsaValidation.ValidateComplaintDecision(new DsaComplaintDecisionRequest { Outcome = "maybe", Facts = "Sprawdzono skargę." });

        Assert.Null(result.Value);
    }

    [Fact]
    public void Subject_hasher_is_unconfigured_without_a_pepper_and_is_stable_when_configured()
    {
        Assert.False(new DsaSubjectHasher(null).IsConfigured);
        Assert.False(new DsaSubjectHasher("   ").IsConfigured);

        var hasher = new DsaSubjectHasher("pepper");
        var first = hasher.Hash("user-1");

        Assert.True(hasher.IsConfigured);
        Assert.Equal(64, first.Length);
        Assert.Equal(first, hasher.Hash("user-1"));
        Assert.NotEqual(first, hasher.Hash("user-2"));
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public void Tokens_are_22_characters_and_differ_between_calls()
    {
        var first = DsaTokens.NewToken();
        var second = DsaTokens.NewToken();

        Assert.Equal(DsaTokens.Length, first.Length);
        Assert.NotEqual(first, second);
        Assert.True(DsaTokens.Matches(first, first));
        Assert.False(DsaTokens.Matches(first, second));
    }
}
