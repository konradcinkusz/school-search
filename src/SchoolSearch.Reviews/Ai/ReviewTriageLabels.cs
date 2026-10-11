using System.Text.Json;

namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// The closed set of advisory labels. A label only orders the moderator queue; it never publishes, rejects, removes
/// or marks a review as automated.
/// </summary>
public static class ReviewTriageLabels
{
    public const string Ok = "ok";
    public const string NeedsAttention = "needs_attention";
    public const string LikelyPersonalData = "likely_personal_data";
    public const string LikelyAbusive = "likely_abusive";
    public const string Unclear = "unclear";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Ok,
        NeedsAttention,
        LikelyPersonalData,
        LikelyAbusive,
        Unclear,
    };
}

/// <summary>A label and a short note. The note is internal and never shown to readers.</summary>
public sealed record ReviewTriageResult(string Label, string Note);

/// <summary>
/// Defensive parser for the model's JSON answer. Anything that is not one object with a label from
/// <see cref="ReviewTriageLabels.All"/> becomes <see cref="ReviewTriageLabels.Unclear"/>. Never throws.
/// </summary>
public static class ReviewTriageParser
{
    public const int MaxNoteLength = 300;

    public static ReviewTriageResult Parse(string? modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return Unclear("empty model output");
        }

        // Tolerates a code fence or a sentence around the object; only the outermost braces are read.
        var start = modelText.IndexOf('{');
        var end = modelText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return Unclear("model output had no JSON object");
        }

        try
        {
            using var document = JsonDocument.Parse(modelText[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("label", out var labelElement)
                || labelElement.ValueKind != JsonValueKind.String)
            {
                return Unclear("model output had no string label");
            }

            var label = labelElement.GetString();
            if (label is null || !ReviewTriageLabels.All.Contains(label))
            {
                return Unclear("model label was outside the allowed set");
            }

            var note = root.TryGetProperty("note", out var noteElement) && noteElement.ValueKind == JsonValueKind.String
                ? noteElement.GetString() ?? string.Empty
                : string.Empty;

            return new ReviewTriageResult(label, SanitizeNote(note));
        }
        catch (JsonException)
        {
            return Unclear("model output was not valid JSON");
        }
    }

    /// <summary>Drops control characters, trims and caps the length, so a note cannot grow the table row or break a log line.</summary>
    internal static string SanitizeNote(string note)
    {
        var cleaned = new string(note.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > MaxNoteLength ? cleaned[..MaxNoteLength] : cleaned;
    }

    private static ReviewTriageResult Unclear(string reason) => new(ReviewTriageLabels.Unclear, reason);
}
