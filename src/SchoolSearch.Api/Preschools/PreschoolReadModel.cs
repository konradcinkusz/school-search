using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Preschools;

/// <summary>
/// Mapping from stored preschool rows to the public DTOs. Preschools carry practical facts only (ADR-0014): no score,
/// rank or grade is read, computed or returned here.
/// </summary>
internal static class PreschoolReadModel
{
    /// <summary>Fixed notice shown on every preschool profile (Polish, user-facing).</summary>
    public const string Notice =
        "Przedszkola nie mają wyniku jakości. Pokazujemy praktyczne informacje: lokalizację, opłaty, godziny i wolne miejsca, każdą ze źródłem i datą.";

    public static PreschoolListItem ToListItem(Preschool preschool, double? distanceKm) => new(
        preschool.Slug,
        preschool.Name,
        preschool.GminaTeryt,
        preschool.GminaName,
        preschool.IsPublic,
        preschool.Latitude,
        preschool.Longitude,
        distanceKm,
        new MoneyRange(preschool.Facts?.MonthlyTuitionMinPln, preschool.Facts?.MonthlyTuitionMaxPln),
        preschool.Facts?.FreePlaces,
        ToFactsBlock(preschool.Facts));

    /// <summary>
    /// Full profile of one preschool. The governing body is a type only, exactly as for schools: the operator name is
    /// never stored and never returned.
    /// </summary>
    public static async Task<PreschoolProfile> BuildProfileAsync(
        CatalogDbContext db,
        Preschool preschool,
        CancellationToken ct)
    {
        GoverningBodyType? bodyType = null;
        if (preschool.GoverningBodyTypeCode is int bodyCode)
        {
            bodyType = await db.GoverningBodyTypes.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Code == bodyCode, ct);
        }

        var governingBody = new GoverningBodyInfo(
            preschool.GoverningBodyTypeCode,
            bodyType?.Label,
            bodyType?.LabelVerified ?? false,
            bodyType?.IsNaturalPerson ?? false);

        return new PreschoolProfile(
            preschool.Slug,
            preschool.Name,
            preschool.GminaTeryt,
            preschool.GminaName,
            preschool.IsPublic,
            preschool.Street,
            preschool.PostalCode,
            preschool.City,
            preschool.Latitude,
            preschool.Longitude,
            governingBody,
            ToFactsBlock(preschool.Facts),
            Notice);
    }

    /// <summary>Null when no facts row exists. Unknown values inside an existing row stay null.</summary>
    public static PreschoolFactsBlock? ToFactsBlock(PreschoolFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        return new PreschoolFactsBlock(
            new MoneyRange(facts.MonthlyTuitionMinPln, facts.MonthlyTuitionMaxPln),
            facts.MonthlyMealPln,
            new OpeningHours(FormatTime(facts.OpensAt), FormatTime(facts.ClosesAt)),
            new FreePlaces(facts.FreePlaces, facts.FreePlacesAsOf),
            new FactSource(
                facts.Provenance.Source,
                facts.Provenance.SourceUrl,
                facts.Provenance.AsOf,
                facts.Provenance.Method,
                facts.Confidence));
    }

    /// <summary>
    /// Case-insensitive substring match on the name. Npgsql uses ILIKE; other providers (InMemory in tests) use a
    /// lower-cased Contains. LIKE wildcards in the term are escaped, so the term is always a literal substring.
    /// </summary>
    public static IQueryable<Preschool> WhereNameContains(IQueryable<Preschool> query, CatalogDbContext db, string term)
    {
        if (db.Database.IsRelational())
        {
            var pattern = "%" + EscapeLike(term) + "%";
            return query.Where(p => EF.Functions.ILike(p.Name, pattern));
        }

        var needle = term.ToLowerInvariant();
        return query.Where(p => p.Name.ToLower().Contains(needle));
    }

    private static string? FormatTime(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
