using CsvHelper;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Catchment.Extract;

/// <summary>
/// Maps a school name as written in a document to an idPodmiot, by EXACT normalised name only (trim, collapse
/// whitespace, lower-case). No fuzzy matching: a name with no exact entry is unmatched, and a name that two different
/// idPodmiot share is ambiguous and matches nothing. Both cases are reported, never guessed.
/// </summary>
public sealed class SchoolNameIndex
{
    private readonly Dictionary<string, long?> _byName;

    private SchoolNameIndex(Dictionary<string, long?> byName) => _byName = byName;

    /// <summary>Entries for the index. Each row is one school; a missing idPodmiot is never accepted.</summary>
    public static SchoolNameIndex FromEntries(IEnumerable<(long IdPodmiot, string Name)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var byName = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var (id, name) in entries)
        {
            var key = Key(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byName.TryGetValue(key, out var existing))
            {
                byName[key] = id;
            }
            else if (existing != id)
            {
                // Two schools share this exact name: never pick one.
                byName[key] = null;
            }
        }

        return new SchoolNameIndex(byName);
    }

    /// <summary>
    /// Reads a delimited file (';' or ',') with a header holding <c>idPodmiot</c> and <c>name</c>. The file is the
    /// operator's export of the school list for the gmina; it must not contain personal-data columns.
    /// </summary>
    public static SchoolNameIndex LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = File.ReadAllText(path);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("School list file is empty.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("School list file has no header row.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "School list");
        var columns = CkeCsvSupport.ResolveColumns(headers, ["idPodmiot", "name"], "School list");

        var entries = new List<(long, string)>();
        var line = 1;
        while (reader.Read())
        {
            line++;
            var idText = CkeCsvSupport.Field(reader, columns["idPodmiot"]).Trim();
            if (!CkeCsvSupport.TryParseExactId(idText, out var id))
            {
                throw new InvalidOperationException($"School list line {line}: idPodmiot must be a plain integer.");
            }

            entries.Add((id, CkeCsvSupport.Field(reader, columns["name"])));
        }

        return FromEntries(entries);
    }

    /// <summary>
    /// The idPodmiot for an exact name match. Returns false with a reason when the name is unknown or ambiguous.
    /// </summary>
    public bool TryMatch(string schoolAsWritten, out long idPodmiot, out string reason)
    {
        idPodmiot = 0;
        var key = Key(schoolAsWritten);
        if (key.Length == 0 || !_byName.TryGetValue(key, out var id))
        {
            reason = "school name matches no entry in the school list (exact match only)";
            return false;
        }

        if (id is null)
        {
            reason = "school name is shared by two idPodmiot values in the school list; not guessed";
            return false;
        }

        idPodmiot = id.Value;
        reason = string.Empty;
        return true;
    }

    /// <summary>Number of distinct normalised names held (ambiguous names included).</summary>
    public int Count => _byName.Count;

    internal static string Key(string name) => string.Join(' ', (name ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
