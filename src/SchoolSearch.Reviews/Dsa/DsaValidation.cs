using System.Text;
using System.Text.RegularExpressions;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>A validated value or the error text. Error text names the field only and never echoes submitted text.</summary>
public sealed record DsaValidated<T>(T? Value, string? Error) where T : class;

public sealed record NoticeInput(
    Guid? ReviewId,
    string ContentReference,
    string Explanation,
    string Category,
    string? SubmitterName,
    string? SubmitterEmail);

public sealed record ComplaintInput(string Explanation, string? Token);

public sealed record DecisionInput(string Action, string LegalOrTermsGround, string Facts);

public sealed record ComplaintDecisionInput(string Outcome, string Facts);

/// <summary>Shape checks for DSA submissions and moderator decisions.</summary>
public static class DsaValidation
{
    public const int MinNoticeExplanation = 20;
    public const int MaxNoticeExplanation = 3000;
    public const int MaxContentReference = 500;
    public const int MaxSubmitterName = 120;
    public const int MaxSubmitterEmail = 200;
    public const int MinComplaintExplanation = 10;
    public const int MaxComplaintExplanation = 2000;
    public const int MinGround = 3;
    public const int MaxGround = 500;
    public const int MinFacts = 10;
    public const int MaxFacts = 3000;
    public const int MaxToken = 64;

    public static readonly IReadOnlySet<string> AllowedNoticeCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        NoticeCategories.IllegalContent,
        NoticeCategories.PersonalData,
        NoticeCategories.Defamation,
        NoticeCategories.Other,
    };

    public static readonly IReadOnlySet<string> ReviewActions = new HashSet<string>(StringComparer.Ordinal)
    {
        ModerationActions.Publish,
        ModerationActions.Reject,
        ModerationActions.Remove,
    };

    public static readonly IReadOnlySet<string> ReplyActions = new HashSet<string>(StringComparer.Ordinal)
    {
        ModerationActions.Publish,
        ModerationActions.Reject,
    };

    public static readonly IReadOnlySet<string> NoticeActions = new HashSet<string>(StringComparer.Ordinal)
    {
        ModerationActions.Keep,
        ModerationActions.Remove,
    };

    public static readonly IReadOnlySet<string> ComplaintOutcomes = new HashSet<string>(StringComparer.Ordinal)
    {
        ComplaintStatuses.Upheld,
        ComplaintStatuses.Reversed,
    };

    /// <summary>Structural check only: one '@', no whitespace, a dot in the domain. Deliverability is not checked.</summary>
    private static readonly Regex EmailShape = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static DsaValidated<NoticeInput> ValidateNotice(DsaNoticeRequest body)
    {
        var reference = Clean(body.ContentReference);
        if (reference is null && body.ReviewId is null)
        {
            return Invalid<NoticeInput>("contentReference or reviewId is required.");
        }

        if (reference is not null && reference.Length > MaxContentReference)
        {
            return Invalid<NoticeInput>($"contentReference must be at most {MaxContentReference} characters.");
        }

        var explanation = Clean(body.Explanation);
        if (explanation is null || explanation.Length < MinNoticeExplanation || explanation.Length > MaxNoticeExplanation)
        {
            return Invalid<NoticeInput>($"explanation must be between {MinNoticeExplanation} and {MaxNoticeExplanation} characters.");
        }

        var category = body.Category?.Trim();
        if (category is null || !AllowedNoticeCategories.Contains(category))
        {
            return Invalid<NoticeInput>("category is not one of the allowed values.");
        }

        if (!body.GoodFaithConfirmed)
        {
            return Invalid<NoticeInput>("goodFaithConfirmed must be true.");
        }

        var name = Clean(body.SubmitterName);
        if (name is not null && name.Length > MaxSubmitterName)
        {
            return Invalid<NoticeInput>($"submitterName must be at most {MaxSubmitterName} characters.");
        }

        var email = Clean(body.SubmitterEmail);
        if (email is not null && (email.Length > MaxSubmitterEmail || !EmailShape.IsMatch(email)))
        {
            return Invalid<NoticeInput>("submitterEmail is not a valid address.");
        }

        var resolvedReference = reference ?? $"review:{body.ReviewId}";
        return new DsaValidated<NoticeInput>(
            new NoticeInput(body.ReviewId, resolvedReference, explanation, category, name, email),
            null);
    }

    public static DsaValidated<ComplaintInput> ValidateComplaint(DsaComplaintRequest body)
    {
        var explanation = Clean(body.Explanation);
        if (explanation is null || explanation.Length < MinComplaintExplanation || explanation.Length > MaxComplaintExplanation)
        {
            return Invalid<ComplaintInput>($"explanation must be between {MinComplaintExplanation} and {MaxComplaintExplanation} characters.");
        }

        var token = Clean(body.Token);
        if (token is not null && token.Length > MaxToken)
        {
            return Invalid<ComplaintInput>("token is not valid.");
        }

        return new DsaValidated<ComplaintInput>(new ComplaintInput(explanation, token), null);
    }

    public static DsaValidated<DecisionInput> ValidateDecision(DsaDecisionRequest body, IReadOnlySet<string> allowedActions)
    {
        var action = body.Action?.Trim().ToLowerInvariant();
        if (action is null || !allowedActions.Contains(action))
        {
            return Invalid<DecisionInput>("action is not one of the allowed values.");
        }

        var ground = Clean(body.LegalOrTermsGround);
        if (ground is null || ground.Length < MinGround || ground.Length > MaxGround)
        {
            return Invalid<DecisionInput>($"legalOrTermsGround must be between {MinGround} and {MaxGround} characters.");
        }

        var facts = Clean(body.Facts);
        if (facts is null || facts.Length < MinFacts || facts.Length > MaxFacts)
        {
            return Invalid<DecisionInput>($"facts must be between {MinFacts} and {MaxFacts} characters.");
        }

        return new DsaValidated<DecisionInput>(new DecisionInput(action, ground, facts), null);
    }

    public static DsaValidated<ComplaintDecisionInput> ValidateComplaintDecision(DsaComplaintDecisionRequest body)
    {
        var outcome = body.Outcome?.Trim().ToLowerInvariant();
        if (outcome is null || !ComplaintOutcomes.Contains(outcome))
        {
            return Invalid<ComplaintDecisionInput>("outcome is not one of the allowed values.");
        }

        var facts = Clean(body.Facts);
        if (facts is null || facts.Length < MinFacts || facts.Length > MaxFacts)
        {
            return Invalid<ComplaintDecisionInput>($"facts must be between {MinFacts} and {MaxFacts} characters.");
        }

        return new DsaValidated<ComplaintDecisionInput>(new ComplaintDecisionInput(outcome, facts), null);
    }

    /// <summary>
    /// Removes control characters (except tab, carriage return and line feed), trims the edges and returns null for
    /// empty text. Nothing is HTML-encoded: output encoding is the consumer's job.
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!char.IsControl(c) || c is '\t' or '\n' or '\r')
            {
                builder.Append(c);
            }
        }

        var text = builder.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static DsaValidated<T> Invalid<T>(string error) where T : class => new(null, error);
}
