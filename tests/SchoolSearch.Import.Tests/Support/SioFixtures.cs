namespace SchoolSearch.Import.Tests;

/// <summary>
/// Header names for the synthetic SIO workbook. The first eight are the UNVERIFIED defaults from the brief (§3.1).
/// <see cref="ClassVIIIPupils"/> is a test-only header: align it with the class-level defaults of
/// <c>SioColumnMap</c> when those are verified. No header here is a personal-data column.
/// </summary>
internal static class SioFixtures
{
    public const string IdPodmiot = "idPodmiot";
    public const string TypPodmiotu = "TypPodmiotu";
    public const string Nazwa = "Nazwa";
    public const string TerytGmina = "TerytGmina";
    public const string Publicznosc = "Publiczność";
    public const string KategoriaUczniow = "KategoriaUczniow";
    public const string SpecyfikaSzkoly = "SpecyfikaSzkoly";
    public const string OrganProwTypeCode = "idTypOrgProw";

    /// <summary>The organ-name column. The loader must never read it: the fixture puts a marker in it to prove that.</summary>
    public const string OrganName = "OrganProwadzacy";

    /// <summary>Test-only class VIII pupil count header. Align with SioColumnMap.</summary>
    public const string ClassVIIIPupils = "Uczniowie klasy VIII";

    public const string PrimaryType = "Szkoła podstawowa";

    /// <summary>A marker that must never appear in any stored field.</summary>
    public const string OrganNameMarker = "ORGAN-NAME-MUST-NOT-BE-STORED";

    public static readonly string[] Headers =
    [
        IdPodmiot,
        TypPodmiotu,
        Nazwa,
        TerytGmina,
        Publicznosc,
        KategoriaUczniow,
        SpecyfikaSzkoly,
        OrganProwTypeCode,
        OrganName,
        ClassVIIIPupils,
    ];

    public static Dictionary<string, string> School(
        string id,
        string name,
        string organTypeCode,
        string pupilsClassVIII = "12") => new()
        {
            [IdPodmiot] = id,
            [TypPodmiotu] = PrimaryType,
            [Nazwa] = name,
            [TerytGmina] = "1465011",
            [Publicznosc] = "publiczna",
            [KategoriaUczniow] = "dzieci",
            [SpecyfikaSzkoly] = "ogolnodostepna",
            [OrganProwTypeCode] = organTypeCode,
            [OrganName] = OrganNameMarker,
            [ClassVIIIPupils] = pupilsClassVIII,
        };
}
