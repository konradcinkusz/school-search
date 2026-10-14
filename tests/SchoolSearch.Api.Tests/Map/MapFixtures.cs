using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Tests.Map;

/// <summary>
/// Synthetic catalog for the map markers tests. Names and figures are invented and ASCII only. The default test box is
/// south 51.9, west 18.9, north 52.1, east 19.1: it contains two schools and three preschools; one school and one
/// preschool lie far outside it, and one school has no coordinates at all.
/// Slugs interleave across kinds on purpose (a, b, c, d, e) so the slug ordering is visible.
/// </summary>
internal static class MapFixtures
{
    public const string SchoolA = "a-szkola-alfa";
    public const string PreschoolB = "b-przedszkole-beta";
    public const string SchoolC = "c-szkola-gamma";
    public const string PreschoolD = "d-przedszkole-delta";
    public const string PreschoolE = "e-przedszkole-epsilon";
    public const string SchoolFar = "y-szkola-daleka";
    public const string PreschoolFar = "z-przedszkole-dalekie";
    public const string SchoolNoCoordinates = "n-szkola-bez-wspolrzednych";

    public const string GminaOne = "1111111";
    public const string GminaTwo = "2222222";

    public const string Box = "south=51.9&west=18.9&north=52.1&east=19.1";

    public static Task ApplyAsync(CatalogDbContext db)
    {
        db.Schools.AddRange(
            School(1, SchoolA, "Szkola Alfa", GminaOne, "Gmina testowa pierwsza", true, 52.00, 19.00),
            School(2, SchoolC, "Szkola Gamma", GminaTwo, "Gmina testowa druga", false, 52.01, 19.01),
            School(3, SchoolFar, "Szkola Daleka", GminaTwo, "Gmina testowa druga", true, 53.5, 20.5),
            School(4, SchoolNoCoordinates, "Szkola Bez Wspolrzednych", GminaOne, "Gmina testowa pierwsza", true, null, null));

        db.Preschools.AddRange(
            Preschool(11, PreschoolB, "Przedszkole Beta", GminaOne, "Gmina testowa pierwsza", true, 52.002, 19.002,
                Facts(11, 200m, 300m, 2)),
            Preschool(12, PreschoolD, "Przedszkole Delta", GminaTwo, "Gmina testowa druga", false, 52.003, 19.003,
                Facts(12, 900m, 1200m, 0)),
            Preschool(13, PreschoolE, "Przedszkole Epsilon", GminaOne, "Gmina testowa pierwsza", true, 52.004, 19.004, null),
            Preschool(14, PreschoolFar, "Przedszkole Dalekie", GminaTwo, "Gmina testowa druga", true, 53.5, 20.5,
                Facts(14, 100m, 150m, 4)));

        return Task.CompletedTask;
    }

    private static School School(long id, string slug, string name, string teryt, string gmina, bool isPublic, double? lat, double? lon) => new()
    {
        IdPodmiot = id,
        Slug = slug,
        Name = name,
        GminaTeryt = teryt,
        GminaName = gmina,
        IsPublic = isPublic,
        Latitude = lat,
        Longitude = lon,
        Provenance = Prov("Synthetic register row"),
    };

    private static Preschool Preschool(long id, string slug, string name, string teryt, string gmina, bool isPublic, double lat, double lon, PreschoolFacts? facts) => new()
    {
        IdPodmiot = id,
        Slug = slug,
        Name = name,
        GminaTeryt = teryt,
        GminaName = gmina,
        IsPublic = isPublic,
        Latitude = lat,
        Longitude = lon,
        Provenance = Prov("Synthetic register row"),
        Facts = facts,
    };

    private static PreschoolFacts Facts(long id, decimal min, decimal max, int free) => new()
    {
        PreschoolId = id,
        MonthlyTuitionMinPln = min,
        MonthlyTuitionMaxPln = max,
        FreePlaces = free,
        FreePlacesAsOf = new DateOnly(2025, 9, 1),
        Confidence = "reported",
        Provenance = Prov("Synthetic tuition table"),
    };

    private static Provenance Prov(string source) => new()
    {
        Source = source,
        SourceUrl = "https://example.com/synthetic-fixture",
        AsOf = new DateOnly(2025, 6, 30),
        Method = "synthetic test",
    };
}
