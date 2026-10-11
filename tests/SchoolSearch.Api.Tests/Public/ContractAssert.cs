using System.Net;
using System.Text.Json;
using Xunit;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>Assertions on the public JSON contract (camelCase names, exact key sets, uniform error envelope).</summary>
internal static class ContractAssert
{
    /// <summary>Asserts the object's property names are exactly <paramref name="expected"/>, order ignored.</summary>
    public static void ExactKeys(JsonElement element, params string[] expected)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var actual = element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>Every property name in the document, at any depth.</summary>
    public static IEnumerable<string> AllPropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in AllPropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in AllPropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    /// <summary>Asserts the uniform error envelope: exactly {"error":{"code","message","traceId"}}.</summary>
    public static void UniformError(JsonElement root, string? expectedCode = null)
    {
        ExactKeys(root, "error");
        var error = root.GetProperty("error");
        ExactKeys(error, "code", "message", "traceId");

        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));

        if (expectedCode is not null)
        {
            Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        }
    }

    /// <summary>Reads a response that must be the uniform error shape with the given status.</summary>
    public static async Task<JsonElement> ErrorResponseAsync(HttpResponseMessage response, HttpStatusCode status, string? expectedCode = null)
    {
        Assert.Equal(status, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        UniformError(root, expectedCode);
        return root;
    }
}

/// <summary>Small navigation helpers over the contract responses.</summary>
internal static class ContractNav
{
    /// <summary>The item of an array whose slug equals <paramref name="slug"/>. Fails the test when absent.</summary>
    public static JsonElement BySlug(JsonElement items, string slug) =>
        items.EnumerateArray().Single(i => i.GetProperty("slug").GetString() == slug);

    /// <summary>The slugs of an array of school items, in response order.</summary>
    public static string[] Slugs(JsonElement items) =>
        items.EnumerateArray().Select(i => i.GetProperty("slug").GetString() ?? string.Empty).ToArray();

    /// <summary>The array's length, read through a local so the xUnit size analyzer does not fire on the call site.</summary>
    public static int Length(JsonElement array) => array.GetArrayLength();
}
