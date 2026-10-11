using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Models;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>A validated, trimmed premium profile.</summary>
public sealed record PremiumDraft(string Headline, string Description, string? WebsiteUrl);

/// <summary>Result of <see cref="PremiumValidator.Validate"/>: a draft, or an error code and a message.</summary>
public sealed record PremiumValidation(PremiumDraft? Draft, string? ErrorCode, string? Message);

/// <summary>
/// Rules for the owner's sponsored text: plain text, length limits, https-only website, and the claim linter.
/// Shape checks run first; the linter runs last and names the matched term.
/// </summary>
public static class PremiumValidator
{
    public const int MaxHeadline = 120;
    public const int MaxDescription = 1500;
    public const int MaxWebsite = 300;

    public static PremiumValidation Validate(PremiumProfileRequest request)
    {
        var headline = request.Headline?.Trim() ?? string.Empty;
        if (headline.Length is 0 or > MaxHeadline)
        {
            return Fail("validation_failed", "Headline must be 1 to 120 characters.");
        }

        if (InputRules.PlainTextProblem(headline, allowNewlines: false) is { } headlineProblem)
        {
            return Fail("validation_failed", headlineProblem);
        }

        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is 0 or > MaxDescription)
        {
            return Fail("validation_failed", "Description must be 1 to 1500 characters.");
        }

        if (InputRules.PlainTextProblem(description, allowNewlines: true) is { } descriptionProblem)
        {
            return Fail("validation_failed", descriptionProblem);
        }

        string? websiteUrl = null;
        var rawUrl = request.WebsiteUrl?.Trim();
        if (!string.IsNullOrEmpty(rawUrl))
        {
            if (rawUrl.Length > MaxWebsite
                || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrEmpty(uri.Host)
                || uri.UserInfo.Length > 0)
            {
                return Fail("validation_failed", "Website must be an https:// address of at most 300 characters.");
            }

            websiteUrl = uri.AbsoluteUri;
        }

        var forbidden = ClaimLinter.Check(headline) ?? ClaimLinter.Check(description);
        if (forbidden is not null)
        {
            return Fail("claim_not_allowed", $"The text contains a term that is not allowed: \"{forbidden}\".");
        }

        return new PremiumValidation(new PremiumDraft(headline, description, websiteUrl), null, null);
    }

    private static PremiumValidation Fail(string code, string message) => new(null, code, message);
}
