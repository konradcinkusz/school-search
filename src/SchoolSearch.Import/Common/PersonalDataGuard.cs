namespace SchoolSearch.Import;

/// <summary>
/// Refuses input that carries personal-data columns (pupil or private-individual names, PESEL, citizenship).
/// Loaders call this on their headers before reading any row, so the file is rejected whole, not row by row.
/// </summary>
public static class PersonalDataGuard
{
    private static readonly string[] ForbiddenHeaders =
    [
        "imie",
        "imię",
        "nazwisko",
        "pesel",
        "obywatelstwo",
        "narodowosc",
        "narodowość",
        "cudzoziemcy",
    ];

    public static void EnsureNoPersonalColumns(IEnumerable<string> headers, string source)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var offending = headers
            .Where(h => ForbiddenHeaders.Contains(h.Trim().ToLowerInvariant()))
            .ToArray();

        if (offending.Length > 0)
        {
            throw new InvalidOperationException(
                $"{source}: refusing to import a file with personal-data column(s): {string.Join(", ", offending)}. " +
                "Re-export without them; no name or personal field is ever stored.");
        }
    }
}
