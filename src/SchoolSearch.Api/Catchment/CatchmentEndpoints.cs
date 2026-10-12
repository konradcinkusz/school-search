using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Services;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Catchment;

/// <summary>
/// GET /api/v1/catchment?gmina=&lt;TERYT&gt;&amp;street=&amp;number=. Answers "which school's catchment (obwód) covers
/// this address according to the gmina's published resolution". It never derives a boundary from distance:
/// without a street list for the gmina the answer is <c>no_data</c>. A listed street whose rules do not cover the
/// house number is <c>no_number_match</c>; a street that is not listed at all is <c>no_match</c>.
/// </summary>
public static class CatchmentEndpoints
{
    public const string Disclaimer =
        "Wynik dotyczy wyłącznie wskazania uchwały gminy; wiążące ustalenie obwodu należy do gminy. " +
        "Zawsze sprawdź treść uchwały pod podanym linkiem.";

    internal const string StateNone = "none";
    internal const string StateStreetList = "street_list";

    internal const string MatchStreetListMatch = "street_list_match";
    internal const string MatchNoMatch = "no_match";
    internal const string MatchNoNumberMatch = "no_number_match";
    internal const string MatchNoData = "no_data";

    private const int MaxStreetLength = 200;

    public static IEndpointRouteBuilder MapCatchmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/catchment", GetCatchment).WithName(CatchmentOperations.GetCatchment);
        return app;
    }

    internal static async Task<IResult> GetCatchment(
        string? gmina,
        string? street,
        string? number,
        CatalogDbContext db,
        SchoolScoreService scores,
        CancellationToken ct)
    {
        var gminaTeryt = gmina?.Trim() ?? string.Empty;
        if (!IsTeryt(gminaTeryt))
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                "Parameter 'gmina' must be a 7-digit TERYT code.");
        }

        var streetKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(street))
        {
            streetKey = StreetNormalizer.Normalize(street);
            if (streetKey.Length == 0 || street.Trim().Length > MaxStreetLength || streetKey.Length > StreetNormalizer.MaxLength)
            {
                return ApiErrorWriter.Result(
                    StatusCodes.Status400BadRequest,
                    "validation_failed",
                    "Parameter 'street' must contain a street name, for example 'ul. Przykładowa'.");
            }
        }

        int? houseNumber = null;
        if (!string.IsNullOrWhiteSpace(number))
        {
            if (!TryParseHouseNumber(number, out var parsed))
            {
                return ApiErrorWriter.Result(
                    StatusCodes.Status400BadRequest,
                    "validation_failed",
                    "Parameter 'number' must start with a house number, for example '12' or '12A'.");
            }

            houseNumber = parsed;
        }

        var coverage = await db.CatchmentCoverages
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.GminaTeryt == gminaTeryt, ct);

        var coverageState = coverage?.State ?? StateNone;
        var coverageDto = new CatchmentCoverageDto(
            coverageState,
            coverage?.ResolutionUrl,
            coverage?.ResolutionDate);

        // No street list (none, missing, polygon or an unknown state): never synthesise a boundary.
        if (coverageState != StateStreetList)
        {
            return TypedResults.Ok(new CatchmentResponse(gminaTeryt, coverageDto, MatchNoData, [], Disclaimer));
        }

        if (streetKey.Length == 0)
        {
            return TypedResults.Ok(new CatchmentResponse(gminaTeryt, coverageDto, MatchNoMatch, [], Disclaimer));
        }

        var rows = await db.CatchmentStreets
            .AsNoTracking()
            .Where(c => c.GminaTeryt == gminaTeryt && c.StreetNormalized == streetKey)
            .ToListAsync(ct);

        var schoolIds = rows
            .Where(row => RowCovers(row, houseNumber))
            .Select(row => row.SchoolId)
            .Distinct()
            .ToList();

        var schools = new List<School>();
        if (schoolIds.Count > 0)
        {
            schools = await db.Schools
                .AsNoTracking()
                .Where(s => schoolIds.Contains(s.IdPodmiot) && s.IsPublic)
                .OrderBy(s => s.Name)
                .ThenBy(s => s.IdPodmiot)
                .ToListAsync(ct);
        }

        var items = await ToItemsAsync(schools, scores, ct);

        // The street is on the list but no rule covers this number: no_number_match. An unknown street, or a
        // rule that covers the number but whose school is not public, stays no_match.
        var match = items.Count > 0 ? MatchStreetListMatch
            : rows.Count > 0 && schoolIds.Count == 0 ? MatchNoNumberMatch
            : MatchNoMatch;

        return TypedResults.Ok(new CatchmentResponse(gminaTeryt, coverageDto, match, items, Disclaimer));
    }

    /// <summary>
    /// Scores the catchment schools the way GET /schools does: the current exam year (the newest with a national
    /// baseline), one batched scoring call, and the shared public mapping in <see cref="SchoolScoreService.ToBlock"/>.
    /// Without a baseline every school is no_baseline, exactly as on /schools.
    /// </summary>
    internal static async Task<List<CatchmentSchoolItem>> ToItemsAsync(
        IReadOnlyList<School> schools,
        SchoolScoreService scores,
        CancellationToken ct)
    {
        var items = new List<CatchmentSchoolItem>(schools.Count);
        if (schools.Count == 0)
        {
            return items;
        }

        var year = await scores.ResolveCurrentYearAsync(ct);
        if (year is not int examYear)
        {
            var noBaseline = SchoolScore.Missing(ScoreStatuses.NoBaseline, null);
            foreach (var school in schools)
            {
                items.Add(CatchmentSchoolItem.From(school, noBaseline));
            }

            return items;
        }

        var ids = schools.Select(school => school.IdPodmiot).ToArray();
        var scoreMap = await scores.ScoreAsync(examYear, ids, ct);
        foreach (var school in schools)
        {
            // A school absent from the map has no exam data for the year: CatchmentSchoolItem.From gives no_exam_data.
            SchoolScore? score = scoreMap.TryGetValue(school.IdPodmiot, out var found) ? found : null;
            items.Add(CatchmentSchoolItem.From(school, score));
        }

        return items;
    }

    /// <summary>
    /// A row covers the address when the number is inside its range (if it has one) and its parity fits
    /// (if it has one). A row with neither covers the whole street, with or without a number. A row with a
    /// range or parity needs a number to match.
    /// </summary>
    internal static bool RowCovers(CatchmentStreet row, int? houseNumber)
    {
        if (row.NumberFrom is not null || row.NumberTo is not null)
        {
            if (houseNumber is not int n)
            {
                return false;
            }

            if (row.NumberFrom is int from && n < from)
            {
                return false;
            }

            if (row.NumberTo is int to && n > to)
            {
                return false;
            }
        }

        if (row.Parity is not null)
        {
            if (houseNumber is not int m)
            {
                return false;
            }

            var expected = m % 2 == 0 ? "even" : "odd";
            if (!string.Equals(row.Parity, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Leading digits only (1 to 6 digits, at least 1). "12A" gives 12; "A12" is refused.</summary>
    internal static bool TryParseHouseNumber(string raw, out int value)
    {
        value = 0;
        var text = raw.Trim();
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits is 0 or > 6)
        {
            return false;
        }

        value = int.Parse(text.AsSpan(0, digits), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        return value >= 1;
    }

    private static bool IsTeryt(string value) =>
        value.Length == 7 && value.All(char.IsAsciiDigit);
}

/// <summary>Stable operation name (generated clients and OpenAPI).</summary>
public static class CatchmentOperations
{
    public const string GetCatchment = "GetCatchment";
}

/// <summary>The coverage of the gmina as stored. "none" when no row exists.</summary>
public sealed record CatchmentCoverageDto(string State, string? ResolutionUrl, DateOnly? ResolutionDate);

public sealed record CatchmentResponse(
    string GminaTeryt,
    CatchmentCoverageDto Coverage,
    string Match,
    IReadOnlyList<CatchmentSchoolItem> Schools,
    string Disclaimer);

/// <summary>
/// Catchment result item, shaped like the public SchoolListItem. scoreStatus and score are produced by the same
/// scoring path as /schools: a published, insufficient_cohort, no_exam_data or no_baseline status, with a complete
/// score block (cohort, interval, methodology version, exam year, source, as-of) whenever the engine ran.
/// </summary>
public sealed record CatchmentSchoolItem(
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    bool IsSpecial,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    string ScoreStatus,
    ScoreBlock? Score)
{
    /// <summary>
    /// <paramref name="score"/> is null when the school has no exam data for the year. The status and block are
    /// read from the score, so this item and /schools cannot disagree.
    /// </summary>
    public static CatchmentSchoolItem From(School school, SchoolScore? score) => new(
        school.Slug,
        school.Name,
        school.GminaTeryt,
        school.GminaName,
        school.IsPublic,
        school.IsSpecial,
        school.Latitude,
        school.Longitude,
        DistanceKm: null,
        ScoreStatus: score?.ScoreStatus ?? ScoreStatuses.NoExamData,
        Score: SchoolScoreService.ToBlock(score));
}
