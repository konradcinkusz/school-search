using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Demo;

/// <summary>
/// Clearly synthetic preschools for the demo gmina <see cref="DemoSeed.GminaTeryt"/>. Names and addresses are fictional,
/// and every row carries <see cref="DemoSeed.Source"/> so it cannot be mistaken for published data. The set mixes public
/// and non-public units, units with and without facts, unknown fields inside a facts row, and one unit with no vacancies.
/// </summary>
/// <remarks>
/// Idempotent: a preschool is added only when its id is missing, and its facts only with it. Runs only through
/// <see cref="DemoSeedHostedService"/>, which refuses to run in Production.
/// </remarks>
public static class DemoPreschoolSeed
{
    private sealed record FactsSpec(
        decimal? TuitionMin,
        decimal? TuitionMax,
        decimal? Meal,
        TimeOnly? Opens,
        TimeOnly? Closes,
        int? FreePlaces,
        string Confidence);

    private sealed record PreschoolSpec(
        long IdPodmiot,
        string Slug,
        string Name,
        bool IsPublic,
        string Street,
        string PostalCode,
        double Latitude,
        double Longitude,
        FactsSpec? Facts);

    private static readonly PreschoolSpec[] Preschools =
    [
        new(910001, "przedszkole-demonstracyjne-nr-1", "Przedszkole Demonstracyjne nr 1 (dane demonstracyjne)", true,
            "ul. Demonstracyjna 1", "99-900", 52.0010, 19.0010,
            new FactsSpec(350m, 480m, 120m, new TimeOnly(6, 30), new TimeOnly(17, 0), 3, "reported")),

        // Zero vacancies: the explicit "no free places" case.
        new(910002, "przedszkole-demonstracyjne-nr-2", "Przedszkole Demonstracyjne nr 2 (dane demonstracyjne)", true,
            "ul. Demonstracyjna 2", "99-900", 52.0020, 19.0020,
            new FactsSpec(0m, 250m, 110m, new TimeOnly(7, 0), new TimeOnly(16, 30), 0, "reported")),

        new(910003, "przedszkole-demonstracyjne-nr-3", "Przedszkole Demonstracyjne nr 3 (niepubliczne, dane demonstracyjne)", false,
            "ul. Przykładowa 3", "99-901", 52.0030, 19.0030,
            new FactsSpec(900m, 1200m, null, new TimeOnly(7, 0), new TimeOnly(18, 0), 5, "unverified")),

        // No facts row at all: the "brak danych" case.
        new(910004, "przedszkole-demonstracyjne-nr-4", "Przedszkole Demonstracyjne nr 4 (niepubliczne, dane demonstracyjne)", false,
            "ul. Przykładowa 4", "99-901", 52.0040, 19.0040,
            null),

        // Facts row with unknown tuition, meal, closing time and vacancies.
        new(910005, "przedszkole-demonstracyjne-nr-5", "Przedszkole Demonstracyjne nr 5 (dane demonstracyjne)", true,
            "ul. Demonstracyjna 5", "99-900", 52.0050, 19.0050,
            new FactsSpec(null, null, null, new TimeOnly(7, 30), null, null, "unverified")),
    ];

    /// <summary>Inserts every demo preschool whose id is missing. Returns the number of rows added (zero on a repeat run).</summary>
    public static async Task<int> ApplyAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var added = 0;
        foreach (var spec in Preschools)
        {
            if (await db.Preschools.AnyAsync(p => p.IdPodmiot == spec.IdPodmiot, cancellationToken))
            {
                continue;
            }

            db.Preschools.Add(Build(spec));
            added += spec.Facts is null ? 1 : 2;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    private static Preschool Build(PreschoolSpec spec) => new()
    {
        IdPodmiot = spec.IdPodmiot,
        Name = spec.Name,
        Slug = spec.Slug,
        GminaTeryt = DemoSeed.GminaTeryt,
        GminaName = DemoSeed.GminaName,
        GoverningBodyTypeCode = null,
        IsPublic = spec.IsPublic,
        Street = spec.Street,
        PostalCode = spec.PostalCode,
        City = DemoSeed.GminaName,
        Latitude = spec.Latitude,
        Longitude = spec.Longitude,
        Provenance = Prov(),
        Facts = spec.Facts is null ? null : BuildFacts(spec.IdPodmiot, spec.Facts),
    };

    private static PreschoolFacts BuildFacts(long preschoolId, FactsSpec facts) => new()
    {
        PreschoolId = preschoolId,
        MonthlyTuitionMinPln = facts.TuitionMin,
        MonthlyTuitionMaxPln = facts.TuitionMax,
        MonthlyMealPln = facts.Meal,
        OpensAt = facts.Opens,
        ClosesAt = facts.Closes,
        FreePlaces = facts.FreePlaces,
        FreePlacesAsOf = facts.FreePlaces is null ? (DateOnly?)null : DemoSeed.AsOf,
        Confidence = facts.Confidence,
        Provenance = Prov(),
    };

    private static Provenance Prov() => new()
    {
        Source = DemoSeed.Source,
        SourceUrl = DemoSeed.SourceUrl,
        AsOf = DemoSeed.AsOf,
        Method = DemoSeed.Method,
    };
}
