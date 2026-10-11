using System.Globalization;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Geo;

/// <summary>
/// Imports school coordinates from a CSV file into School.Latitude, School.Longitude and School.CoordinatesProvenance.
/// No geocoder is called or implied: the coordinates come from the file (ADR-0010 is still pending).
/// </summary>
/// <remarks>
/// Required columns: <c>idPodmiot, lat, lon, source, sourceUrl, asOf, method</c>. The provenance comes from the
/// row itself: <c>asOf</c> is an ISO date (yyyy-MM-dd). A row is rejected when the school does not match exactly,
/// when a coordinate is not a finite number, when lat is outside 49..55 or lon outside 14..25 (Poland), or when
/// a provenance field is missing or malformed. Re-running is idempotent.
/// </remarks>
public static class GeoLoader
{
    public const double MinLatitude = 49.0;
    public const double MaxLatitude = 55.0;
    public const double MinLongitude = 14.0;
    public const double MaxLongitude = 25.0;

    private const string AsOfFormat = "yyyy-MM-dd";

    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        TimeProvider clock,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(clock);

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("Geo import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("Import aborted: the file has no header row. Nothing was written.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "Geo import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "Geo import");

        var columns = CkeCsvSupport.ResolveColumns(
            headers,
            new[] { "idPodmiot", "lat", "lon", "source", "sourceUrl", "asOf", "method" },
            "Geo import");

        var schools = (await db.Schools.ToListAsync(ct)).ToDictionary(s => s.IdPodmiot);
        var log = new RejectionLog();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var rowsRead = 0;
        var rowsWritten = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;
            var row = rowsRead;

            var idText = CkeCsvSupport.Field(reader, columns["idPodmiot"]);
            if (!CkeCsvSupport.TryParseExactId(idText, out var id))
            {
                log.Add($"row {row}: idPodmiot is not a plain integer; no fuzzy matching");
                continue;
            }

            if (!seenIds.Add(idText))
            {
                log.Add($"row {row}: duplicate idPodmiot in file; the row is rejected");
                continue;
            }

            if (!schools.TryGetValue(id, out var school))
            {
                log.Add($"row {row}: idPodmiot matches no School; exact match only");
                continue;
            }

            if (!CkeCsvSupport.TryParseDecimal(CkeCsvSupport.Field(reader, columns["lat"]), out var latitude)
                || latitude < MinLatitude || latitude > MaxLatitude)
            {
                log.Add($"row {row}: lat must be a number from {MinLatitude.ToString(CultureInfo.InvariantCulture)} to {MaxLatitude.ToString(CultureInfo.InvariantCulture)} (Poland)");
                continue;
            }

            if (!CkeCsvSupport.TryParseDecimal(CkeCsvSupport.Field(reader, columns["lon"]), out var longitude)
                || longitude < MinLongitude || longitude > MaxLongitude)
            {
                log.Add($"row {row}: lon must be a number from {MinLongitude.ToString(CultureInfo.InvariantCulture)} to {MaxLongitude.ToString(CultureInfo.InvariantCulture)} (Poland)");
                continue;
            }

            var source = CkeCsvSupport.Field(reader, columns["source"]).Trim();
            var sourceUrl = CkeCsvSupport.Field(reader, columns["sourceUrl"]).Trim();
            var method = CkeCsvSupport.Field(reader, columns["method"]).Trim();

            if (CkeCsvSupport.IsBlank(source) || CkeCsvSupport.IsBlank(sourceUrl) || CkeCsvSupport.IsBlank(method))
            {
                log.Add($"row {row}: source, sourceUrl and method must all be filled");
                continue;
            }

            if (!CkeCsvSupport.IsAbsoluteHttpUrl(sourceUrl))
            {
                log.Add($"row {row}: sourceUrl is not an absolute http(s) URL");
                continue;
            }

            if (!CkeCsvSupport.FitsProvenance(source, sourceUrl, method))
            {
                log.Add($"row {row}: provenance text is too long for storage");
                continue;
            }

            if (!DateOnly.TryParseExact(
                    CkeCsvSupport.Field(reader, columns["asOf"]).Trim(),
                    AsOfFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var asOf))
            {
                log.Add($"row {row}: asOf must be a date in yyyy-MM-dd form");
                continue;
            }

            school.Latitude = latitude;
            school.Longitude = longitude;
            school.CoordinatesProvenance = CkeCsvSupport.NewProvenance(source, sourceUrl, asOf, method);
            rowsWritten++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(rowsRead, rowsWritten, log.Count, log.Reasons());
    }
}
