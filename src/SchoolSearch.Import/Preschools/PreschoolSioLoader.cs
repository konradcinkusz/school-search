using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Preschools;

/// <summary>
/// Loads the SIO preschool units (CSV) into <see cref="Preschool"/> rows. Practical identity only: no score, no rank.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Required: the mapped core columns (see <see cref="PreschoolSioColumnMap"/>). A missing one aborts the run before
/// any write. A personal-data or name column aborts the whole file.</item>
/// <item>Keeps only rows whose TypPodmiotu equals the map value. Other rows are counted in RowsRead, neither written nor rejected.</item>
/// <item>A row with an unparsable value, a duplicate idPodmiot, a name or address longer than its column, or a bad TERYT
/// code is rejected with a reason. Nothing is fuzzy-matched.</item>
/// <item>Idempotent. Upserted by IdPodmiot. A stored slug is never changed. Coordinates are not in the SIO file and are
/// never written here, so values loaded by another step are preserved.</item>
/// <item>Only the governing body TYPE code is stored. The operator name is never read.</item>
/// </list>
/// </remarks>
public static class PreschoolSioLoader
{
    public const string SourceName = "SIO";
    public const string SourceUrl = "https://dane.gov.pl/pl/dataset/288";
    public const string Method = "csv import";

    private const string DictionarySource = "SIO dictionary (idTypOrgProw), unverified";
    private const string DictionaryMethod = "code seen in SIO file; not in seed list; unverified";
    private const int GminaTerytLength = 7;
    private const int MaxNameLength = 300;
    private const int MaxStreetLength = 200;
    private const int MaxPostalCodeLength = 10;
    private const int MaxCityLength = 120;
    private const int MaxGminaNameLength = 120;

    /// <param name="asOf">The date the file describes. Defaults to the UTC date of <paramref name="clock"/>.</param>
    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        PreschoolSioColumnMap map,
        TimeProvider clock,
        CancellationToken ct,
        DateOnly? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(clock);
        map.Validate();

        var provenanceDate = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("Preschool SIO import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("Preschool SIO import aborted: the file has no header row. Nothing was written.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "Preschool SIO import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "Preschool SIO import");

        var columns = ResolveColumns(headers, map);

        var existing = await db.Preschools.ToDictionaryAsync(p => p.IdPodmiot, ct);
        var knownGoverningBodyCodes = (await db.GoverningBodyTypes.AsNoTracking().Select(g => g.Code).ToListAsync(ct)).ToHashSet();

        foreach (var entry in GoverningBodyDictionary.Entries)
        {
            if (knownGoverningBodyCodes.Add(entry.Code))
            {
                db.GoverningBodyTypes.Add(new GoverningBodyType
                {
                    Code = entry.Code,
                    Label = entry.Label,
                    LabelVerified = entry.LabelVerified,
                    IsNaturalPerson = entry.IsNaturalPerson,
                    Provenance = NewDictionaryProvenance(provenanceDate),
                });
            }
        }

        var log = new RejectionLog();
        var seenIds = new HashSet<long>();
        var rowsRead = 0;
        var rowsWritten = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;
            var row = rowsRead + 1;

            var typeText = Field(reader, columns.TypPodmiotu);
            if (!string.Equals(typeText, map.PreschoolType.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idText = Field(reader, columns.IdPodmiot);
            if (!CkeCsvSupport.TryParseExactId(idText, out var idPodmiot))
            {
                log.Add($"row {row}: '{map.IdPodmiot}' is not a plain integer; no fuzzy matching");
                continue;
            }

            if (!seenIds.Add(idPodmiot))
            {
                log.Add($"row {row}: duplicate idPodmiot {idPodmiot} in the file; the row is rejected");
                continue;
            }

            var name = Field(reader, columns.Name);
            if (name.Length == 0)
            {
                log.Add($"row {row}: '{map.Name}' is empty");
                continue;
            }

            if (name.Length > MaxNameLength)
            {
                log.Add($"row {row}: '{map.Name}' is longer than {MaxNameLength} characters");
                continue;
            }

            if (!TryParseTeryt(Field(reader, columns.TerytGmina), out var gminaTeryt))
            {
                log.Add($"row {row}: '{map.TerytGmina}' is not a 7-digit TERYT code");
                continue;
            }

            if (!TryParseOptionalCode(Field(reader, columns.OrganProwTypeCode), out var governingBodyCode))
            {
                log.Add($"row {row}: '{map.OrganProwTypeCode}' is not a whole number");
                continue;
            }

            var gminaName = OptionalText(reader, columns.GminaName);
            if (gminaName is not null && gminaName.Length > MaxGminaNameLength)
            {
                log.Add($"row {row}: '{map.GminaName}' is longer than {MaxGminaNameLength} characters");
                continue;
            }

            var street = OptionalText(reader, columns.Street);
            if (street is not null && street.Length > MaxStreetLength)
            {
                log.Add($"row {row}: '{map.Street}' is longer than {MaxStreetLength} characters");
                continue;
            }

            var postalCode = OptionalText(reader, columns.PostalCode);
            if (postalCode is not null && postalCode.Length > MaxPostalCodeLength)
            {
                log.Add($"row {row}: '{map.PostalCode}' is longer than {MaxPostalCodeLength} characters");
                continue;
            }

            var city = OptionalText(reader, columns.City);
            if (city is not null && city.Length > MaxCityLength)
            {
                log.Add($"row {row}: '{map.City}' is longer than {MaxCityLength} characters");
                continue;
            }

            var isPublic = string.Equals(Field(reader, columns.Publicznosc), map.PublicValue.Trim(), StringComparison.OrdinalIgnoreCase);

            // The row parsed completely: write it.
            var isNew = !existing.TryGetValue(idPodmiot, out var preschool);
            if (isNew)
            {
                preschool = new Preschool
                {
                    IdPodmiot = idPodmiot,
                    Slug = SchoolSlug.Create(name, idPodmiot),
                };
                existing[idPodmiot] = preschool;
                db.Preschools.Add(preschool);
            }

            preschool!.Name = name;
            preschool.GminaTeryt = gminaTeryt;
            preschool.GminaName = gminaName;
            preschool.GoverningBodyTypeCode = governingBodyCode;
            preschool.IsPublic = isPublic;
            preschool.Street = street;
            preschool.PostalCode = postalCode;
            preschool.City = city;
            preschool.Provenance = CkeCsvSupport.NewProvenance(SourceName, SourceUrl, provenanceDate, Method);

            if (governingBodyCode is int code && knownGoverningBodyCodes.Add(code))
            {
                // Unknown code: a dictionary row is created unverified, with no label, so the public page cannot invent one.
                db.GoverningBodyTypes.Add(new GoverningBodyType
                {
                    Code = code,
                    Label = null,
                    LabelVerified = false,
                    IsNaturalPerson = GoverningBodyDictionary.IsNaturalPerson(code),
                    Provenance = NewDictionaryProvenance(provenanceDate),
                });
            }

            rowsWritten++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(rowsRead, rowsWritten, log.Count, log.Reasons());
    }

    private static PreschoolColumns ResolveColumns(IReadOnlyList<string> headers, PreschoolSioColumnMap map)
    {
        var required = new List<string>
        {
            map.IdPodmiot,
            map.TypPodmiotu,
            map.Name,
            map.TerytGmina,
            map.Publicznosc,
            map.OrganProwTypeCode,
        };
        // Optional columns join the required list only when the map names them.

        foreach (var optional in new[] { map.GminaName, map.Street, map.PostalCode, map.City })
        {
            if (optional is not null)
            {
                required.Add(optional);
            }
        }

        var resolved = CkeCsvSupport.ResolveColumns(headers, required, "Preschool SIO import");

        int? Optional(string? name) => name is null ? null : resolved[name];

        return new PreschoolColumns(
            IdPodmiot: resolved[map.IdPodmiot],
            TypPodmiotu: resolved[map.TypPodmiotu],
            Name: resolved[map.Name],
            TerytGmina: resolved[map.TerytGmina],
            Publicznosc: resolved[map.Publicznosc],
            OrganProwTypeCode: resolved[map.OrganProwTypeCode],
            GminaName: Optional(map.GminaName),
            Street: Optional(map.Street),
            PostalCode: Optional(map.PostalCode),
            City: Optional(map.City));
    }

    private static string Field(CsvReader reader, int index) =>
        CkeCsvSupport.Field(reader, index).Trim();

    /// <summary>Trimmed text, or null when the column is not mapped or the cell is blank.</summary>
    private static string? OptionalText(CsvReader reader, int? index)
    {
        if (index is not int column)
        {
            return null;
        }

        var text = Field(reader, column);
        return text.Length == 0 ? null : text;
    }

    /// <summary>Empty is null. A purely numeric value shorter than 7 is zero-padded. Otherwise it must be 7 digits.</summary>
    private static bool TryParseTeryt(string text, out string? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!text.All(char.IsAsciiDigit) || text.Length > GminaTerytLength)
        {
            return false;
        }

        value = text.PadLeft(GminaTerytLength, '0');
        return true;
    }

    /// <summary>Empty is null. Otherwise a non-negative whole number.</summary>
    private static bool TryParseOptionalCode(string text, out int? code)
    {
        code = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!CkeCsvSupport.TryParseCount(text, out var value) || value < 0)
        {
            return false;
        }

        code = value;
        return true;
    }

    private static Provenance NewDictionaryProvenance(DateOnly asOf) =>
        CkeCsvSupport.NewProvenance(DictionarySource, SourceUrl, asOf, DictionaryMethod);

    private sealed record PreschoolColumns(
        int IdPodmiot,
        int TypPodmiotu,
        int Name,
        int TerytGmina,
        int Publicznosc,
        int OrganProwTypeCode,
        int? GminaName,
        int? Street,
        int? PostalCode,
        int? City);
}
