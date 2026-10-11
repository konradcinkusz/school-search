using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Strict, defensive reading of the model's answer. The answer must be exactly one JSON object with exactly the five
/// expected keys, closed vocabularies, a boolean and a plain-text draft that passes the number grounding check and the
/// personal-data guard. Any deviation returns null: the caller then answers with a safe fallback, never partial trust.
/// </summary>
public static class CorrectionTriageParser
{
    public const int MaxDraftLength = 1500;

    private static readonly string[] RequiredKeys = ["category", "fieldLooksValid", "severity", "suggestedAction", "draftReplyPl"];

    // A score_dispute draft may not promise that a score will move (AI-FEATURES feature 5, B1). Conservative on purpose.
    private static readonly Regex ScorePromise = new(
        @"\b(zmieni|podwyższ|obniż|poprawi|podniesi)\w*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <param name="modelText">Raw text of the model answer.</param>
    /// <param name="factsJson">The facts payload the model was given; the draft's digits must occur in it.</param>
    /// <returns>A validated draft, or null when anything is off.</returns>
    public static CorrectionTriageDraft? Parse(string modelText, string factsJson)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(modelText.Trim());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactKeys(root))
            {
                return null;
            }

            var category = Vocabulary(root, "category", CorrectionTriageVocabulary.Categories);
            var severity = Vocabulary(root, "severity", CorrectionTriageVocabulary.Severities);
            var action = Vocabulary(root, "suggestedAction", CorrectionTriageVocabulary.SuggestedActions);
            if (category is null || severity is null || action is null)
            {
                return null;
            }

            if (!root.TryGetProperty("fieldLooksValid", out var validElement)
                || validElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            if (!root.TryGetProperty("draftReplyPl", out var draftElement) || draftElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var draft = draftElement.GetString()?.Trim() ?? string.Empty;
            if (draft.Length == 0 || draft.Length > MaxDraftLength || draft.Contains('<') || draft.Contains('>'))
            {
                return null;
            }

            if (CorrectionTriagePreSendGuard.ContainsPersonalData(draft) || !NumberGuard.IsGrounded(draft, factsJson))
            {
                return null;
            }

            if (category == CorrectionTriageVocabulary.CategoryScoreDispute && ScorePromise.IsMatch(draft))
            {
                return null;
            }

            // Score disputes always go to the methodology process, whatever the model proposed (B1, B8).
            var effectiveAction = category == CorrectionTriageVocabulary.CategoryScoreDispute
                ? CorrectionTriageVocabulary.ActionRouteToMethodology
                : action;

            return new CorrectionTriageDraft(category, validElement.ValueKind == JsonValueKind.True, severity, effectiveAction, draft);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasExactKeys(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return false;
            }
        }

        return names.SetEquals(RequiredKeys);
    }

    private static string? Vocabulary(JsonElement root, string name, IReadOnlySet<string> allowed)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        return value is not null && allowed.Contains(value) ? value : null;
    }
}
