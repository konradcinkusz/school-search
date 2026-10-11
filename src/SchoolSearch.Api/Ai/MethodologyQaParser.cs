using System.Text.Json;

namespace SchoolSearch.Api.Ai;

/// <summary>A parsed, validated model answer. <see cref="CitedSections"/> are all known headings of the document.</summary>
public sealed record MethodologyDraft(string Answer, bool Covered, IReadOnlyList<string> CitedSections);

/// <summary>
/// Defensive parser for the model's JSON answer. Anything unexpected returns null, which the service treats as rejected
/// output: a partial or invented answer is never trusted. A citation that is not a heading of the document rejects the whole answer.
/// </summary>
public static class MethodologyQaParser
{
    public const int MaxAnswerChars = 1200;

    public static MethodologyDraft? Parse(string modelText, MethodologyDocument document)
    {
        var start = modelText.IndexOf('{', StringComparison.Ordinal);
        var end = modelText.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(modelText.Substring(start, end - start + 1));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("covered", out var coveredElement)
                || coveredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("answer", out var answerElement)
                || answerElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var covered = coveredElement.ValueKind == JsonValueKind.True;
            var cited = ReadCitations(root, document);
            if (cited is null)
            {
                return null;
            }

            if (!covered)
            {
                return new MethodologyDraft(MethodologyQaPromptBuilder.NotCoveredText, false, []);
            }

            var answer = answerElement.GetString()?.Trim() ?? string.Empty;
            if (answer.Length == 0
                || answer.Length > MaxAnswerChars
                || answer.Contains('<', StringComparison.Ordinal)
                || answer.Contains('>', StringComparison.Ordinal)
                || cited.Count == 0)
            {
                return null;
            }

            return new MethodologyDraft(answer, true, cited);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Known, distinct citations in model order; null when the array is malformed or names a heading that does not exist.</summary>
    private static List<string>? ReadCitations(JsonElement root, MethodologyDocument document)
    {
        var cited = new List<string>();
        if (!root.TryGetProperty("citedSections", out var array))
        {
            return cited;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var heading = MethodologyDocument.NormalizeHeading(item.GetString() ?? string.Empty);
            if (!document.IsKnownHeading(heading))
            {
                return null;
            }

            if (!cited.Contains(heading, StringComparer.Ordinal))
            {
                cited.Add(heading);
            }
        }

        return cited;
    }
}
