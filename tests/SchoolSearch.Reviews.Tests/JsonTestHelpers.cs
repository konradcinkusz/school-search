using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Sdk;

namespace SchoolSearch.Reviews.Tests;

internal static class JsonTestHelpers
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>JSON content with camelCase names, as the BFF sends them.</summary>
    public static JsonContent JsonBody(object value) => JsonContent.Create(value, options: Options);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>Reads the uniform error envelope and returns its machine code.</summary>
    public static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var root = await ReadJsonAsync(response);
        return root.GetProperty("error").GetProperty("code").GetString()
            ?? throw new XunitException("Error code missing: " + root.GetRawText());
    }
}
