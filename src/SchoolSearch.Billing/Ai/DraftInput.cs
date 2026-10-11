namespace SchoolSearch.Billing.Ai;

/// <summary>The request body of the draft endpoint: the owner's bullet facts. Nothing else is accepted from the owner.</summary>
public sealed record ProfileDraftRequest(string[]? Bullets);

/// <summary>
/// Shape checks on the owner's bullet facts: 1 to 10 bullets, each 1 to 200 characters of plain text. A bullet is
/// trimmed; a control character (a line break included) is refused, so one bullet is one line of text.
/// </summary>
public static class DraftInputValidator
{
    public const int MaxBullets = 10;
    public const int MaxBulletLength = 200;

    /// <summary>Returns the trimmed bullets, or a Polish message that the owner can read. Never throws.</summary>
    public static (IReadOnlyList<string>? Bullets, string? Error) Validate(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return (null, "Podaj od 1 do 10 punktów z faktami o szkole.");
        }

        if (raw.Count > MaxBullets)
        {
            return (null, "Można podać najwyżej 10 punktów z faktami.");
        }

        var cleaned = new List<string>(raw.Count);
        foreach (var item in raw)
        {
            var bullet = (item ?? string.Empty).Trim();
            if (bullet.Length == 0)
            {
                return (null, "Punkt z faktami nie może być pusty.");
            }

            if (bullet.Length > MaxBulletLength)
            {
                return (null, $"Punkt z faktami może mieć najwyżej {MaxBulletLength} znaków.");
            }

            if (bullet.Any(char.IsControl))
            {
                return (null, "Punkty z faktami mogą zawierać wyłącznie zwykły tekst w jednym wierszu.");
            }

            cleaned.Add(bullet);
        }

        return (cleaned, null);
    }
}
