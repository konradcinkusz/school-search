using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Search;

namespace SchoolSearch.Api.Map;

/// <summary>
/// Public, anonymous map markers under /api/v1/map/markers. The global rate limiter applies as for every other public
/// read. Markers are ordered by slug and carry no score, rank, review or billing data. Malformed or inconsistent
/// parameters are refused with 400 validation_failed; q shorter than two characters is ignored, as in the lists.
/// </summary>
public static class MapEndpoints
{
    internal const int DefaultLimit = 300;
    internal const int MaxLimit = 500;
    internal const int MaxQueryLength = 100;
    internal const string CacheControl = "public, max-age=60";
    internal const string PreschoolOnlyFiltersMessage = "maxTuition and hasFreePlaces apply to preschools only.";

    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();

        api.MapGet("/map/markers", GetMarkers).WithName(MapOperations.GetMapMarkers);

        return app;
    }

    /// <summary>
    /// Query: south, west, north, east (required, south below north, west below east), kind (school, preschool or all;
    /// default all), q, gmina (TERYT), publicOnly (true keeps public units; false or absent keeps all), maxTuition (PLN,
    /// preschools only), hasFreePlaces (true keeps preschools with free places; preschools only), limit (default 300,
    /// clamped to 1..500). With kind=all, maxTuition and hasFreePlaces narrow the preschools and leave no schools
    /// (a school has no tuition or places); with kind=school they are refused.
    /// </summary>
    internal static async Task<IResult> GetMarkers(
        string? south,
        string? west,
        string? north,
        string? east,
        string? kind,
        string? q,
        string? gmina,
        string? publicOnly,
        string? maxTuition,
        string? hasFreePlaces,
        string? limit,
        HttpContext http,
        MapMarkerService markers,
        CancellationToken ct)
    {
        if (!TryParseCoordinate(south, -90d, 90d, out var southValue)
            || !TryParseCoordinate(north, -90d, 90d, out var northValue))
        {
            return Invalid("Parametry south i north są wymagane: szerokość geograficzna od -90 do 90.");
        }

        if (!TryParseCoordinate(west, -180d, 180d, out var westValue)
            || !TryParseCoordinate(east, -180d, 180d, out var eastValue))
        {
            return Invalid("Parametry west i east są wymagane: długość geograficzna od -180 do 180.");
        }

        if (southValue >= northValue)
        {
            return Invalid("Parametr south musi być mniejszy niż north.");
        }

        if (westValue >= eastValue)
        {
            return Invalid("Parametr west musi być mniejszy niż east.");
        }

        var kindText = Clean(kind)?.ToLowerInvariant() ?? "all";
        if (kindText is not ("school" or "preschool" or "all"))
        {
            return Invalid("Parametr kind przyjmuje wartości school, preschool albo all.");
        }

        var teryt = Clean(gmina);
        if (teryt is not null && !(teryt.Length == 7 && teryt.All(char.IsAsciiDigit)))
        {
            return Invalid("Parametr gmina musi być kodem TERYT gminy (7 cyfr).");
        }

        var term = Clean(q);
        if (term is not null && term.Length > MaxQueryLength)
        {
            return Invalid("Parametr q może mieć najwyżej 100 znaków.");
        }

        if (term is not null && term.Length < SearchEndpoints.MinQueryLength)
        {
            term = null;
        }

        if (!TryParseFlag(publicOnly, out var publicFlag))
        {
            return Invalid("Parametr publicOnly przyjmuje wartości true albo false.");
        }

        if (!TryParseFlag(hasFreePlaces, out var freeFlag))
        {
            return Invalid("Parametr hasFreePlaces przyjmuje wartości true albo false.");
        }

        decimal? ceiling = null;
        var maxText = Clean(maxTuition);
        if (maxText is not null)
        {
            if (!int.TryParse(maxText, NumberStyles.None, CultureInfo.InvariantCulture, out var maxValue))
            {
                return Invalid("Parametr maxTuition musi być kwotą w złotych, liczbą całkowitą nieujemną.");
            }

            ceiling = maxValue;
        }

        var limitValue = DefaultLimit;
        var limitText = Clean(limit);
        if (limitText is not null)
        {
            if (!int.TryParse(limitText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedLimit))
            {
                return Invalid("Parametr limit musi być liczbą całkowitą.");
            }

            limitValue = Math.Clamp(parsedLimit, 1, MaxLimit);
        }

        var preschoolOnlyFilters = ceiling is not null || freeFlag;
        if (kindText == "school" && preschoolOnlyFilters)
        {
            return Invalid(PreschoolOnlyFiltersMessage);
        }

        var query = new MapMarkerQuery(
            southValue,
            westValue,
            northValue,
            eastValue,
            kindText != "preschool" && !preschoolOnlyFilters,
            kindText != "school",
            term,
            teryt,
            publicFlag,
            ceiling,
            freeFlag,
            limitValue);

        var response = await markers.GetMarkersAsync(query, ct);
        http.Response.Headers.CacheControl = CacheControl;
        return TypedResults.Ok(response);
    }

    private static bool TryParseCoordinate(string? text, double min, double max, out double value)
    {
        var clean = Clean(text);
        if (clean is not null
            && double.TryParse(clean.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max)
        {
            return true;
        }

        value = 0d;
        return false;
    }

    /// <summary>Absent is false; "true" and "false" are accepted; anything else is refused.</summary>
    private static bool TryParseFlag(string? text, out bool value)
    {
        value = false;
        var clean = Clean(text);
        return clean is null || bool.TryParse(clean, out value);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Invalid(string message) =>
        ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", message);
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class MapOperations
{
    public const string GetMapMarkers = "GetMapMarkers";
}
