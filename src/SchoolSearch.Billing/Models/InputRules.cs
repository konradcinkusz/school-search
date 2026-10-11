namespace SchoolSearch.Billing.Models;

/// <summary>
/// Input rules shared by the claim, premium and moderation endpoints. Each returns null when the value is acceptable,
/// otherwise the first problem in English (the Polish UI maps codes, not these messages).
/// </summary>
public static class InputRules
{
    public const int MaxSlugLength = 200;
    public const int MaxRoleLength = 120;
    public const int MaxNotesLength = 500;

    public static string? Slug(string? slug) =>
        string.IsNullOrWhiteSpace(slug) || slug.Trim().Length > MaxSlugLength
            ? "School slug is missing or too long."
            : null;

    public static string? RoleDescription(string? role)
    {
        var trimmed = role?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxRoleLength)
        {
            return "Role description must be 1 to 120 characters.";
        }

        return PlainTextProblem(trimmed, allowNewlines: false);
    }

    public static string? Notes(string? notes)
    {
        var trimmed = notes?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxNotesLength)
        {
            return "Notes must be at most 500 characters.";
        }

        return PlainTextProblem(trimmed, allowNewlines: true);
    }

    /// <summary>
    /// Plain text only: no control characters (line breaks allowed only where asked) and no markup brackets.
    /// The output is never rendered as HTML; this keeps stored text honest and easy to review.
    /// </summary>
    public static string? PlainTextProblem(string value, bool allowNewlines)
    {
        foreach (var ch in value)
        {
            if (ch == '<' || ch == '>')
            {
                return "Plain text only: angle brackets are not allowed.";
            }

            if (char.IsControl(ch) && !(allowNewlines && (ch == '\n' || ch == '\r' || ch == '\t')))
            {
                return "Plain text only: control characters are not allowed.";
            }
        }

        return null;
    }
}
