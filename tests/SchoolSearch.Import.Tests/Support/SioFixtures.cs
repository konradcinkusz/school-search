namespace SchoolSearch.Import.Tests;

/// <summary>
/// Synthetic SIO workbook layout. Headers come from <see cref="SioColumnMap"/> itself, so the fixture follows the
/// map's (UNVERIFIED) defaults and the class-level placeholders. A test that needs a different layout passes its own map.
/// Values are invented: school names only, no people.
/// </summary>
internal static class SioFixtures
{
    public static readonly SioColumnMap Map = new();

    /// <summary>The organ-name column. The loader never reads it; the marker below must never be stored.</summary>
    public const string OrganName = "OrganProwadzacy";

    public const string OrganNameMarker = "ORGAN-NAME-MUST-NOT-BE-STORED";

    /// <summary>Synthetic personal-data header used to prove the whole file is refused.</summary>
    public const string PersonalDataHeader = "imie";

    public static readonly string Nazwa = Map.Name;

    public static readonly string[] Headers = Map.RequiredHeaders().Append(OrganName).ToArray();

    /// <summary>One primary-school row. Class VIII carries <paramref name="pupilsClassVIII"/>; other levels are zero.</summary>
    public static Dictionary<string, string> School(
        string id,
        string name,
        string organTypeCode,
        string pupilsClassVIII = "12")
    {
        var row = new Dictionary<string, string>
        {
            [Map.IdPodmiot] = id,
            [Map.TypPodmiotu] = Map.PrimarySchoolType,
            [Map.Name] = name,
            [Map.TerytGmina] = "1465011",
            [Map.Publicznosc] = Map.PublicValue,
            [Map.KategoriaUczniow] = "dzieci",
            [Map.SpecyfikaSzkoly] = "ogolnodostepna",
            [Map.OrganProwTypeCode] = organTypeCode,
            [OrganName] = OrganNameMarker,
        };

        foreach (var level in SioColumnMap.ClassLevels)
        {
            var isEighth = level == "VIII";
            row[Map.PupilColumns[level]] = isEighth ? pupilsClassVIII : "0";
            row[Map.OddzialColumns[level]] = isEighth ? "1" : "0";
        }

        return row;
    }
}
