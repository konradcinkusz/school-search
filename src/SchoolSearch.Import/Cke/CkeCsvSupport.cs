using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Import.Cke;

/// <summary>
/// Shared CSV plumbing for the CKE, baseline and geo loaders: reading, delimiter detection, exact
/// column lookup, culture-invariant number parsing and the person-name guard.
/// </summary>
internal static class CkeCsvSupport
{
    /// <summary>Rejection lines kept in the report. Further rejections are counted and summarised in one line.</summary>
    public const int MaxStoredRejectionReasons = 200;

    /// <summary>Header fragments that mark a pupil or person name column. Matched after lower-casing and removing diacritics and non-letters.</summary>
    private static readonly string[] PersonNameFragments = ["imie", "nazwisko", "uczen"];

    public static async Task<string> ReadTextAsync(Stream stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        ct.ThrowIfCancellationRequested();
        var text = await reader.ReadToEndAsync();
        ct.ThrowIfCancellationRequested();
        return text;
    }

    /// <summary>Picks ';' or ',' from the header line, whichever occurs more often. Used by the baseline and geo loaders.</summary>
    public static string DetectDelimiter(string text)
    {
        var firstLine = text.TrimStart('﻿', '\r', '\n').Split('\n', 2)[0];
        var semicolons = firstLine.Count(c => c == ';');
        var commas = firstLine.Count(c => c == ',');
        return semicolons > commas ? ";" : ",";
    }

    public static CsvConfiguration CreateConfiguration(string delimiter) =>
        new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = delimiter,
            TrimOptions = TrimOptions.None,
            MissingFieldFound = null,
            HeaderValidated = null,
        };

    /// <summary>
    /// Refuses the whole file when a header looks like a pupil or person name column. Runs before any row is read.
    /// The message names the column position, not the header text.
    /// </summary>
    public static void EnsureNoPersonNameColumns(IReadOnlyList<string> headers, string source)
    {
        ArgumentNullException.ThrowIfNull(headers);

        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = Normalize(headers[i]);
            if (PersonNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"{source}: import aborted. Column #{i + 1} looks like a pupil or person name column. " +
                    "Pupil names must never be read or stored. Remove that column from the export and run again. Nothing was written.");
            }
        }
    }

    /// <summary>Index of the first header equal to <paramref name="name"/> (trimmed, case-insensitive), or -1.</summary>
    public static int FindColumn(IReadOnlyList<string> headers, string name)
    {
        var wanted = name.Trim();
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Maps every required name to its column index. Missing names abort the run before anything is written.
    /// </summary>
    public static Dictionary<string, int> ResolveColumns(IReadOnlyList<string> headers, IEnumerable<string> required, string source)
    {
        var resolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var name in required)
        {
            if (resolved.ContainsKey(name))
            {
                continue;
            }

            var index = FindColumn(headers, name);
            if (index < 0)
            {
                missing.Add(name);
            }
            else
            {
                resolved[name] = index;
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{source}: import aborted. Missing required column(s): {string.Join(", ", missing)}. Nothing was written.");
        }

        return resolved;
    }

    /// <summary>The raw field at <paramref name="index"/>. An absent column (index -1) or a short row gives an empty string.</summary>
    public static string Field(CsvReader reader, int index)
    {
        if (index < 0)
        {
            return string.Empty;
        }

        try
        {
            return reader.GetField(index) ?? string.Empty;
        }
        catch (IndexOutOfRangeException)
        {
            return string.Empty;
        }
    }

    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Exact integer text only (digits, no sign, no spaces, no leading zeros): the value must round-trip,
    /// so "0123" does not match idPodmiot 123.
    /// </summary>
    public static bool TryParseExactId(string raw, out long id)
    {
        id = 0;
        if (IsBlank(raw) || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), raw, StringComparison.Ordinal))
        {
            return false;
        }

        id = parsed;
        return true;
    }

    /// <summary>
    /// A finite decimal written with a point or a single comma ("45,3" and "45.3" both give 45.3).
    /// Values with both separators, or with thousands separators, are refused rather than guessed.
    /// </summary>
    public static bool TryParseDecimal(string raw, out double value)
    {
        value = 0;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || (trimmed.Contains('.') && trimmed.Contains(',')))
        {
            return false;
        }

        var normalized = trimmed.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>A whole number of zero or more is not checked here; the caller checks the sign.</summary>
    public static bool TryParseCount(string raw, out int value) =>
        int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static Provenance NewProvenance(string source, string sourceUrl, DateOnly asOf, string method) =>
        new()
        {
            Source = source,
            SourceUrl = sourceUrl,
            AsOf = asOf,
            Method = method,
        };

    /// <summary>True when the provenance fields fit their database columns (200 / 500 / 500 characters).</summary>
    public static bool FitsProvenance(string source, string sourceUrl, string method) =>
        source.Length is > 0 and <= 200 && sourceUrl.Length is > 0 and <= 500 && method.Length is > 0 and <= 500;

    public static bool IsAbsoluteHttpUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Normalize(string header)
    {
        var decomposed = header.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || !char.IsLetter(c))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

/// <summary>Collects rejection reasons. Counts every rejection; keeps the first <see cref="CkeCsvSupport.MaxStoredRejectionReasons"/> lines.</summary>
internal sealed class RejectionLog
{
    private readonly List<string> _stored = [];

    public int Count { get; private set; }

    public void Add(string reason)
    {
        Count++;
        if (_stored.Count < CkeCsvSupport.MaxStoredRejectionReasons)
        {
            _stored.Add(reason);
        }
    }

    public IReadOnlyList<string> Reasons()
    {
        var list = new List<string>(_stored);
        if (Count > _stored.Count)
        {
            list.Add($"... {Count - _stored.Count} more rejected rows not listed");
        }

        return list;
    }
}
