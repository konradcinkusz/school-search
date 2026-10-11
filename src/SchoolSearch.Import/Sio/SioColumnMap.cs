using System;
using System.Collections.Generic;
using System.Linq;

namespace SchoolSearch.Import;

/// <summary>
/// Column and value names of the SIO "Szkoly" xlsx (dane.gov.pl dataset 288) as the loader reads them.
/// </summary>
/// <remarks>
/// Every default in this record is UNVERIFIED. It comes from the product brief (docs/MASTER-PROMPT.md, Data)
/// and has not been checked against the published file: the publisher host is unreachable from the build
/// environment. The loader never guesses. A mapped header missing from the file aborts the run before any
/// write, and the error lists every missing header. The placeholder class-level columns deliberately do not
/// match any real header, so a run with the defaults fails fast until the map is set from the real file.
/// The organ-prowadzacy NAME column is intentionally not part of this map: it is never read.
/// </remarks>
public sealed record SioColumnMap
{
    /// <summary>The class levels of a primary school, in order. Keys of the class-level dictionaries.</summary>
    public static readonly IReadOnlyList<string> ClassLevels = new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

    // UNVERIFIED header names (brief §3.1).
    public string IdPodmiot { get; init; } = "idPodmiot";

    public string TypPodmiotu { get; init; } = "TypPodmiotu";

    public string Name { get; init; } = "Nazwa";

    public string TerytGmina { get; init; } = "TerytGmina";

    public string Publicznosc { get; init; } = "Publiczność";

    /// <summary>Required to be present. Its value is not stored (no contract field exists for it).</summary>
    public string KategoriaUczniow { get; init; } = "KategoriaUczniow";

    public string SpecyfikaSzkoly { get; init; } = "SpecyfikaSzkoly";

    /// <summary>idTypOrgProw. Stored as the governing body type code only.</summary>
    public string OrganProwTypeCode { get; init; } = "idTypOrgProw";

    // UNVERIFIED value names. Compared case-insensitively after trimming and NFC normalisation.

    /// <summary>Value of TypPodmiotu that selects primary schools (brief: "Szkoła podstawowa").</summary>
    public string PrimarySchoolType { get; init; } = "Szkoła podstawowa";

    /// <summary>Value of Publicznosc that sets IsPublic to true.</summary>
    public string PublicValue { get; init; } = "publiczna";

    /// <summary>A SpecyfikaSzkoly value containing this text (case-insensitive) sets IsSpecial to true.</summary>
    public string SpecialSchoolMarker { get; init; } = "specjaln";

    /// <summary>Class level to pupil-count header. UNVERIFIED placeholders until the real header row is checked.</summary>
    public IReadOnlyDictionary<string, string> PupilColumns { get; init; } = PlaceholderColumns("pupils");

    /// <summary>Class level to division (oddzialy) count header. UNVERIFIED placeholders.</summary>
    public IReadOnlyDictionary<string, string> OddzialColumns { get; init; } = PlaceholderColumns("oddzialy");

    /// <summary>Every header the loader needs. A file missing any of them is rejected before any write.</summary>
    public IReadOnlyList<string> RequiredHeaders()
    {
        var headers = new List<string>
        {
            IdPodmiot,
            TypPodmiotu,
            Name,
            TerytGmina,
            Publicznosc,
            KategoriaUczniow,
            SpecyfikaSzkoly,
            OrganProwTypeCode,
        };
        headers.AddRange(PupilColumns.Values);
        headers.AddRange(OddzialColumns.Values);
        return headers;
    }

    /// <summary>Checks the map itself (not the file). Throws <see cref="ArgumentException"/> on an empty or inconsistent map.</summary>
    public void Validate()
    {
        var scalars = new[]
        {
            nameof(IdPodmiot), IdPodmiot,
            nameof(TypPodmiotu), TypPodmiotu,
            nameof(Name), Name,
            nameof(TerytGmina), TerytGmina,
            nameof(Publicznosc), Publicznosc,
            nameof(KategoriaUczniow), KategoriaUczniow,
            nameof(SpecyfikaSzkoly), SpecyfikaSzkoly,
            nameof(OrganProwTypeCode), OrganProwTypeCode,
            nameof(PrimarySchoolType), PrimarySchoolType,
            nameof(PublicValue), PublicValue,
            nameof(SpecialSchoolMarker), SpecialSchoolMarker,
        };

        for (var i = 0; i < scalars.Length; i += 2)
        {
            if (string.IsNullOrWhiteSpace(scalars[i + 1]))
            {
                throw new ArgumentException($"SioColumnMap.{scalars[i]} must not be empty.");
            }
        }

        if (PupilColumns.Count == 0)
        {
            throw new ArgumentException("SioColumnMap.PupilColumns must map at least one class level.");
        }

        if (!PupilColumns.Keys.ToHashSet().SetEquals(OddzialColumns.Keys))
        {
            throw new ArgumentException("SioColumnMap.PupilColumns and OddzialColumns must cover the same class levels.");
        }

        foreach (var header in PupilColumns.Values.Concat(OddzialColumns.Values))
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new ArgumentException("SioColumnMap class-level header names must not be empty.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> PlaceholderColumns(string kind) =>
        ClassLevels.ToDictionary(level => level, level => $"UNVERIFIED-{kind}-{level}");
}
