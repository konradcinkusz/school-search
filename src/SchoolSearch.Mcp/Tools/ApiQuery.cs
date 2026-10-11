using System.Globalization;

namespace SchoolSearch.Mcp.Tools;

/// <summary>Builds the query part of an Api path. Null values are omitted; everything else is escaped.</summary>
internal static class ApiQuery
{
    public static string Build(params (string Key, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => p.Value is not null)
            .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!));

        var query = string.Join("&", parts);
        return query.Length == 0 ? string.Empty : "?" + query;
    }

    public static string? Number(double? value) =>
        value?.ToString("0.###################", CultureInfo.InvariantCulture);

    public static string? Integer(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    public static string? Flag(bool? value) =>
        value is null ? null : value.Value ? "true" : "false";
}
