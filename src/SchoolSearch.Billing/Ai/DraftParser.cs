using System.Text.Json;

namespace SchoolSearch.Billing.Ai;

/// <summary>A headline and description the model proposed, after the defensive parse. Plain text, within the profile limits.</summary>
public sealed record DraftCandidate(string Headline, string Description);

/// <summary>
/// Defensive parser for the model's JSON answer. Anything that is not one object with a non-empty headline and
/// description inside the profile limits becomes null: nothing is truncated and nothing is repaired. Never throws.
/// </summary>
public static class DraftParser
{
    public const int MaxHeadlineLength = 120;
    public const int MaxDescriptionLength = 1500;

    public static DraftCandidate? Parse(string? modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return null;
        }

        // Tolerates a code fence or a sentence around the object; only the outermost braces are read.
        var start = modelText.IndexOf('{');
        var end = modelText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(modelText[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("headline", out var headlineElement)
                || headlineElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("description", out var descriptionElement)
                || descriptionElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var headline = Plain(headlineElement.GetString());
            var description = Plain(descriptionElement.GetString());
            if (headline.Length == 0
                || description.Length == 0
                || headline.Length > MaxHeadlineLength
                || description.Length > MaxDescriptionLength
                || IsMarkup(headline)
                || IsMarkup(description))
            {
                return null;
            }

            return new DraftCandidate(headline, description);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Control characters become spaces and runs of whitespace collapse, so the text is one plain paragraph.</summary>
    internal static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var spaced = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The profile is plain text. Angle brackets mean markup, so such a draft is refused rather than cleaned.</summary>
    private static bool IsMarkup(string text) => text.Contains('<') || text.Contains('>');
}
