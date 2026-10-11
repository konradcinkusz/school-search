using System.Text;
using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Corrections;

/// <summary>
/// Raw POST body. Everything is optional at the type level; <see cref="CorrectionRequestValidator"/> decides what is
/// required. Kind is optional: absent or empty means a school, so existing clients are unchanged.
/// </summary>
public sealed record CorrectionSubmission(string? Slug, string? Field, string? Message, string? ContactEmail, string? Kind = null);

/// <summary>A submission after normalization. The message is stored verbatim apart from removed control characters and trimmed edges.</summary>
public sealed record NormalizedCorrection(string Slug, string Field, string Message, string? ContactEmail, string Kind = CorrectionTargetKinds.School);

/// <summary>The kinds of published record a correction may target. Public values, as the API takes and returns them.</summary>
public static class CorrectionTargetKinds
{
    public const string School = "school";
    public const string Preschool = "preschool";
}

public sealed record CorrectionValidation(NormalizedCorrection? Value, string? Error);

/// <summary>
/// Shape checks for correction submissions. Error text names the offending field only and never echoes
/// the submitted value (the message and the contact email are personal or free text).
/// </summary>
public static class CorrectionRequestValidator
{
    public const int MaxSlugLength = 120;
    public const int MinMessageLength = 10;
    public const int MaxMessageLength = 2000;
    public const int MaxRawMessageLength = 8000;
    public const int MaxContactEmailLength = 200;

    /// <summary>Fixed allowlist of correctable fields. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> AllowedFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "name",
        "governing_body",
        "cohort",
        "address",
        "fee",
        "attribute",
        "score",
        "methodology",
        "other",
    };

    /// <summary>Fixed allowlist of correctable preschool facts (kind "preschool"). Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> AllowedPreschoolFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "tuition_min",
        "tuition_max",
        "hours",
        "free_places",
        "address",
        "other",
    };

    /// <summary>Structural check only: one '@', no whitespace, a dot in the domain. Deliverability is not checked.</summary>
    private static readonly Regex EmailShape = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static CorrectionValidation Validate(CorrectionSubmission input)
    {
        var slug = input.Slug?.Trim();
        if (string.IsNullOrEmpty(slug))
        {
            return Invalid("slug is required.");
        }

        var kind = string.IsNullOrWhiteSpace(input.Kind) ? CorrectionTargetKinds.School : input.Kind.Trim().ToLowerInvariant();
        if (kind is not (CorrectionTargetKinds.School or CorrectionTargetKinds.Preschool))
        {
            return Invalid("kind is not one of the allowed values.");
        }

        var field = input.Field?.Trim().ToLowerInvariant();
        var allowedFields = kind == CorrectionTargetKinds.Preschool ? AllowedPreschoolFields : AllowedFields;
        if (field is null || !allowedFields.Contains(field))
        {
            return Invalid("field is not one of the allowed values.");
        }

        var rawMessage = input.Message;
        if (rawMessage is null)
        {
            return Invalid("message is required.");
        }

        if (rawMessage.Length > MaxRawMessageLength)
        {
            return Invalid($"message must be between {MinMessageLength} and {MaxMessageLength} characters.");
        }

        var message = NormalizeMessage(rawMessage);
        if (message.Length < MinMessageLength || message.Length > MaxMessageLength)
        {
            return Invalid($"message must be between {MinMessageLength} and {MaxMessageLength} characters.");
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(input.ContactEmail))
        {
            email = input.ContactEmail.Trim();
            if (email.Length > MaxContactEmailLength || !EmailShape.IsMatch(email))
            {
                return Invalid("contactEmail is not a valid address.");
            }
        }

        return new CorrectionValidation(new NormalizedCorrection(slug, field, message, email, kind), null);
    }

    /// <summary>
    /// Removes control characters (Unicode category Cc) except tab, carriage return and line feed,
    /// then trims. Nothing is HTML-encoded: output encoding is the consumer's job.
    /// </summary>
    public static string NormalizeMessage(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!char.IsControl(c) || c is '\t' or '\n' or '\r')
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }

    private static CorrectionValidation Invalid(string error) => new(null, error);
}
