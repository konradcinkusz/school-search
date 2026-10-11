using System.Collections.Generic;
using System.Linq;

namespace SchoolSearch.Import;

/// <summary>
/// Seed rows for the idTypOrgProw (governing body type) dictionary.
/// </summary>
/// <remarks>
/// The official code list is OPEN (docs/data/SOURCES.md, 3.2). The codes below are the ones named in the
/// product brief. Labels are NOT inferred: they stay null and <c>LabelVerified</c> is false until the official
/// list is checked. <c>IsNaturalPerson</c> is true for 141, 146 and 150 per the brief, which is still unverified.
/// Names of governing bodies are never stored, so this flag only controls how a type is presented.
/// </remarks>
public static class GoverningBodyDictionary
{
    public sealed record Entry(int Code, string? Label, bool LabelVerified, bool IsNaturalPerson);

    private static readonly int[] SeededCodes = new[] { 120, 125, 130, 131, 132, 133, 135, 140, 141, 143, 144, 146, 148, 150 };

    private static readonly int[] NaturalPersonCodes = new[] { 141, 146, 150 };

    public static IReadOnlyList<Entry> Entries { get; } = SeededCodes
        .Select(code => new Entry(code, Label: null, LabelVerified: false, IsNaturalPerson: NaturalPersonCodes.Contains(code)))
        .ToArray();

    /// <summary>True when the code is a seeded natural-person code (brief: 141, 146, 150). Unverified.</summary>
    public static bool IsNaturalPerson(int code) => NaturalPersonCodes.Contains(code);
}
