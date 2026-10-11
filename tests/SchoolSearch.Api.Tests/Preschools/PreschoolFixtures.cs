using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Tests.Preschools;

/// <summary>
/// Synthetic preschools for the preschool contract tests. Names, addresses and figures are invented; no unit is real.
/// Coordinates: Alfa, Delta and Beta lie near (52.0, 19.0), Beta about 5.6 km north of it; Gamma lies about 110 km away.
/// </summary>
internal static class PreschoolFixtures
{
    public const string Alfa = "przedszkole-alfa";
    public const string Beta = "przedszkole-beta";
    public const string Gamma = "przedszkole-gamma";
    public const string Delta = "przedszkole-delta";

    public const string GminaOne = "1111111";
    public const string GminaTwo = "2222222";

    /// <summary>The fixed notice, verbatim from the public contract.</summary>
    public const string Notice =
        "Przedszkola nie mają wyniku jakości. Pokazujemy praktyczne informacje: lokalizację, opłaty, godziny i wolne miejsca, każdą ze źródłem i datą.";

    /// <summary>Adds the synthetic catalog. Synchronous work only; the factory saves the context.</summary>
    public static Task ApplyAsync(CatalogDbContext db)
    {
        db.GoverningBodyTypes.Add(new GoverningBodyType
        {
            Code = 141,
            Label = null,
            LabelVerified = false,
            IsNaturalPerson = true,
            Provenance = Prov("Synthetic governing body type", new DateOnly(2025, 6, 30)),
        });

        db.Preschools.AddRange(
            new Preschool
            {
                IdPodmiot = 1,
                Slug = Alfa,
                Name = "Przedszkole Alfa",
                GminaTeryt = GminaOne,
                GminaName = "Gmina testowa pierwsza",
                GoverningBodyTypeCode = 130,
                IsPublic = true,
                Street = "ul. Testowa 1",
                PostalCode = "99-999",
                City = "Testowo",
                Latitude = 52.0,
                Longitude = 19.0,
                Provenance = Prov("Synthetic register row", new DateOnly(2025, 6, 30)),
                Facts = new PreschoolFacts
                {
                    PreschoolId = 1,
                    MonthlyTuitionMinPln = 300m,
                    MonthlyTuitionMaxPln = 450m,
                    MonthlyMealPln = 100m,
                    OpensAt = new TimeOnly(6, 30),
                    ClosesAt = new TimeOnly(17, 0),
                    FreePlaces = 2,
                    FreePlacesAsOf = new DateOnly(2025, 9, 1),
                    Confidence = "reported",
                    Provenance = Prov("Synthetic tuition table", new DateOnly(2025, 8, 15)),
                },
            },
            new Preschool
            {
                IdPodmiot = 2,
                Slug = Beta,
                Name = "Przedszkole Beta",
                GminaTeryt = GminaOne,
                GminaName = "Gmina testowa pierwsza",
                GoverningBodyTypeCode = 141,
                IsPublic = false,
                Street = "ul. Testowa 2",
                PostalCode = "99-999",
                City = "Testowo",
                Latitude = 52.05,
                Longitude = 19.0,
                Provenance = Prov("Synthetic register row", new DateOnly(2025, 6, 30)),
                Facts = new PreschoolFacts
                {
                    PreschoolId = 2,
                    MonthlyTuitionMinPln = 800m,
                    MonthlyTuitionMaxPln = 900m,
                    OpensAt = new TimeOnly(7, 0),
                    ClosesAt = new TimeOnly(18, 0),
                    FreePlaces = 0,
                    FreePlacesAsOf = new DateOnly(2025, 9, 1),
                    Confidence = "reported",
                    Provenance = Prov("Synthetic tuition table", new DateOnly(2025, 8, 15)),
                },
            },
            new Preschool
            {
                IdPodmiot = 3,
                Slug = Gamma,
                Name = "Przedszkole Gamma",
                GminaTeryt = GminaTwo,
                GminaName = "Gmina testowa druga",
                GoverningBodyTypeCode = null,
                IsPublic = true,
                Street = "ul. Testowa 3",
                PostalCode = "99-998",
                City = "Testowo Wschód",
                Latitude = 53.0,
                Longitude = 20.0,
                Provenance = Prov("Synthetic register row", new DateOnly(2025, 6, 30)),
                Facts = null,
            },
            new Preschool
            {
                IdPodmiot = 4,
                Slug = Delta,
                Name = "Przedszkole Delta",
                GminaTeryt = GminaOne,
                GminaName = "Gmina testowa pierwsza",
                GoverningBodyTypeCode = null,
                IsPublic = true,
                Street = null,
                PostalCode = null,
                City = null,
                Latitude = 52.001,
                Longitude = 19.001,
                Provenance = Prov("Synthetic register row", new DateOnly(2025, 6, 30)),
                Facts = new PreschoolFacts
                {
                    PreschoolId = 4,
                    MonthlyTuitionMinPln = null,
                    MonthlyTuitionMaxPln = null,
                    MonthlyMealPln = null,
                    OpensAt = null,
                    ClosesAt = null,
                    FreePlaces = 5,
                    FreePlacesAsOf = new DateOnly(2025, 9, 1),
                    Confidence = "unverified",
                    Provenance = Prov("Synthetic submission", new DateOnly(2025, 9, 1)),
                },
            });

        return Task.CompletedTask;
    }

    private static Provenance Prov(string source, DateOnly asOf) => new()
    {
        Source = source,
        SourceUrl = "https://example.com/synthetic-fixture",
        AsOf = asOf,
        Method = "synthetic test",
    };
}
