namespace SchoolSearch.Import.Cke;

/// <summary>
/// Column names of the CKE per-school exam export. Every default is UNVERIFIED: the CKE export has not
/// been inspected from the build environment (docs/data/SOURCES.md, OPEN items 3.3 and 3.6). Replace the
/// defaults with a JSON override (<c>cke --map</c>) once the real header row is known.
/// </summary>
/// <remarks>
/// <see cref="Key"/> holds the SIO idPodmiot as exact integer text and is joined by exact match only.
/// Its default column name "rspo" is a guess; whether CKE publishes a stable key at all is ADR-0012 (pending).
/// A row whose key matches no School is rejected and counted; there is no fuzzy matching.
/// A subject with both cells empty is skipped (the school did not sit that exam). A subject with only one
/// cell filled, or a value outside its range, rejects the whole row.
/// </remarks>
public sealed record CkeColumnMap
{
    /// <summary>The shared default map, used when no JSON override is given.</summary>
    public static CkeColumnMap Defaults { get; } = new();

    /// <summary>Join key column (exact idPodmiot). UNVERIFIED default.</summary>
    public string Key { get; init; } = "rspo";

    /// <summary>Field delimiter. UNVERIFIED default: Polish spreadsheet exports usually use ';' with decimal commas.</summary>
    public string Delimiter { get; init; } = ";";

    /// <summary>Mean percent of the Polish exam. UNVERIFIED default.</summary>
    public string PolishMeanPercent { get; init; } = "polish_mean";

    /// <summary>Number of pupils who sat the Polish exam. UNVERIFIED default.</summary>
    public string PolishPupilsSat { get; init; } = "polish_sat";

    /// <summary>Mean percent of the mathematics exam. UNVERIFIED default.</summary>
    public string MathematicsMeanPercent { get; init; } = "math_mean";

    /// <summary>Number of pupils who sat the mathematics exam. UNVERIFIED default.</summary>
    public string MathematicsPupilsSat { get; init; } = "math_sat";

    /// <summary>Mean percent of the foreign-language exam. UNVERIFIED default.</summary>
    public string ForeignLanguageMeanPercent { get; init; } = "foreign_mean";

    /// <summary>Number of pupils who sat the foreign-language exam. UNVERIFIED default.</summary>
    public string ForeignLanguagePupilsSat { get; init; } = "foreign_sat";

    /// <summary>Throws when the map cannot be used: blank names, duplicate column names, or a delimiter that is not one character.</summary>
    public void Validate()
    {
        if (Delimiter is null || Delimiter.Length != 1)
        {
            throw new InvalidOperationException("Invalid CKE column map: Delimiter must be exactly one character.");
        }

        var names = new List<string> { Key };
        names.AddRange(SubjectColumns().SelectMany(s => new[] { s.MeanPercentColumn, s.PupilsSatColumn }));

        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Invalid CKE column map: every column name must be non-blank.");
        }

        var duplicates = names
            .GroupBy(n => n.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Invalid CKE column map: column name(s) used more than once: {string.Join(", ", duplicates)}.");
        }
    }

    /// <summary>The mapped subject columns, in a fixed order.</summary>
    internal IReadOnlyList<CkeSubjectColumns> SubjectColumns() =>
    [
        new(ExamSubjects.Polish, PolishMeanPercent, PolishPupilsSat),
        new(ExamSubjects.Mathematics, MathematicsMeanPercent, MathematicsPupilsSat),
        new(ExamSubjects.ForeignLanguage, ForeignLanguageMeanPercent, ForeignLanguagePupilsSat),
    ];
}

/// <summary>One exam subject with its two mapped columns.</summary>
internal sealed record CkeSubjectColumns(string Subject, string MeanPercentColumn, string PupilsSatColumn);
