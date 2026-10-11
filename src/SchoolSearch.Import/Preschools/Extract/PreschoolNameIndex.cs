using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>One preschool from the <c>--preschools</c> list.</summary>
public sealed record PreschoolRef(long IdPodmiot, string Name);

/// <summary>The parsed list, its rejected lines, and how many rows the file had.</summary>
public sealed record PreschoolListLoad(PreschoolNameIndex Index, IReadOnlyList<string> Rejects, int RowsRead);

/// <summary>
/// Exact name lookup for the preschool list. The only normalisation is case, whitespace, typographic quotes and dashes,
/// and Unicode form. Diacritics are kept, so "Przedszkole Źródełko" and "Przedszkole Zrodelko" never match.
/// There is no fuzzy matching: a name that is missing or matches two entries is never resolved by guessing.
/// </summary>
public sealed partial class PreschoolNameIndex
{
    private readonly Dictionary<string, List<PreschoolRef>> _byName;

    private PreschoolNameIndex(Dictionary<string, List<PreschoolRef>> byName)
    {
        _byName = byName;
    }

    /// <summary>Every list entry whose normalised name equals the normalised text. Empty when none, several when ambiguous.</summary>
    public IReadOnlyList<PreschoolRef> Find(string nameAsWritten)
    {
        ArgumentNullException.ThrowIfNull(nameAsWritten);
        if (_byName.TryGetValue(Normalize(nameAsWritten), out var refs))
        {
            return refs;
        }

        return Array.Empty<PreschoolRef>();
    }

    /// <summary>
    /// Reads a CSV with <c>idPodmiot</c> and <c>name</c> (or <c>Nazwa</c>). When the file has <c>TerytGmina</c>, only rows
    /// of <paramref name="gminaTeryt"/> are used. Personal-data columns refuse the whole file.
    /// </summary>
    public static PreschoolListLoad Load(string text, string gminaTeryt)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(gminaTeryt);

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("--preschools has no header row. Nothing was written.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();
        PersonalDataGuard.EnsureNoPersonalColumns(headers, "preschool-extract --preschools");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "preschool-extract --preschools");

        var idColumn = FindHeader(headers, "idPodmiot");
        var nameColumn = FindHeader(headers, "name");
        if (nameColumn < 0)
        {
            nameColumn = FindHeader(headers, "Nazwa");
        }

        var terytColumn = FindHeader(headers, "TerytGmina");

        if (idColumn < 0 || nameColumn < 0)
        {
            throw new InvalidOperationException("--preschools needs the columns idPodmiot and name (or Nazwa). Nothing was written.");
        }

        var byName = new Dictionary<string, List<PreschoolRef>>(StringComparer.Ordinal);
        var rejects = new List<string>();
        var seenIds = new HashSet<long>();
        var rowsRead = 0;

        while (reader.Read())
        {
            rowsRead++;
            var row = rowsRead + 1;

            if (terytColumn >= 0 && !string.Equals(CkeCsvSupport.Field(reader, terytColumn).Trim(), gminaTeryt, StringComparison.Ordinal))
            {
                continue;
            }

            var idText = CkeCsvSupport.Field(reader, idColumn).Trim();
            if (!CkeCsvSupport.TryParseExactId(idText, out var id))
            {
                rejects.Add($"list row {row}: idPodmiot is not a plain integer");
                continue;
            }

            if (!seenIds.Add(id))
            {
                rejects.Add($"list row {row}: idPodmiot {id} appears twice in the list");
                continue;
            }

            var name = CkeCsvSupport.Field(reader, nameColumn).Trim();
            if (name.Length == 0)
            {
                rejects.Add($"list row {row}: name is empty");
                continue;
            }

            var key = Normalize(name);
            if (!byName.TryGetValue(key, out var refs))
            {
                refs = [];
                byName[key] = refs;
            }

            refs.Add(new PreschoolRef(id, name));
        }

        return new PreschoolListLoad(new PreschoolNameIndex(byName), rejects, rowsRead);
    }

    /// <summary>Case-insensitive, whitespace-collapsed, typographic quotes and dashes mapped to ASCII. Nothing else changes.</summary>
    public static string Normalize(string value)
    {
        var text = value.Normalize(NormalizationForm.FormC)
            .Replace('„', '"')
            .Replace('”', '"')
            .Replace('“', '"')
            .Replace('«', '"')
            .Replace('»', '"')
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('–', '-')
            .Replace('—', '-');
        return WhitespaceRun().Replace(text, " ").Trim().ToLowerInvariant();
    }

    private static int FindHeader(IReadOnlyList<string> headers, string name)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
