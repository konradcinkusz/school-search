using System.Net;
using System.Text.Json;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// Anti-goal (METRIC-ETHICS section 1: no ascending or bottom ranking). Checked against the document the Api
/// publishes at /openapi/v1.json, so the contract is what is tested: no operation may accept a parameter, or a
/// request-body property, named like a sort direction.
/// </summary>
public sealed class OpenApiSortParameterTests
{
    private static readonly HashSet<string> DirectionNames = new(StringComparer.Ordinal)
    {
        "order", "dir", "direction", "asc", "desc", "ascending", "descending", "reverse", "sortorder", "sortdirection",
    };

    [Fact]
    public async Task No_operation_declares_a_sort_direction_parameter_or_body_property()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await JsonTestHelpers.ReadJsonAsync(response);
        var paths = document.GetProperty("paths");

        // Not vacuous: the public read operations are documented, so the scan covers real operations.
        Assert.True(paths.TryGetProperty("/api/v1/schools", out _), "/api/v1/schools is not in the OpenAPI document.");
        Assert.True(paths.TryGetProperty("/api/v1/ranking", out _), "/api/v1/ranking is not in the OpenAPI document.");

        var components = document.TryGetProperty("components", out var componentsElement) ? componentsElement : default;
        var offenders = new List<string>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var entry in path.Value.EnumerateObject())
            {
                if (entry.Name == "parameters")
                {
                    // Path-level parameters apply to every operation of the path.
                    CollectParameterOffenders(path.Name, entry.Value, offenders);
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (entry.Value.TryGetProperty("parameters", out var parameters))
                {
                    CollectParameterOffenders($"{path.Name} {entry.Name}", parameters, offenders);
                }

                CollectBodyOffenders($"{path.Name} {entry.Name}", entry.Value, components, offenders);
            }
        }

        Assert.Empty(offenders);
    }

    private static void CollectParameterOffenders(string where, JsonElement parameters, List<string> offenders)
    {
        if (parameters.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.TryGetProperty("name", out var name) && IsDirectionName(name.GetString()))
            {
                offenders.Add($"{where}: parameter '{name.GetString()}'");
            }
        }
    }

    private static void CollectBodyOffenders(string where, JsonElement operation, JsonElement components, List<string> offenders)
    {
        if (!operation.TryGetProperty("requestBody", out var requestBody)
            || !requestBody.TryGetProperty("content", out var content))
        {
            return;
        }

        foreach (var media in content.EnumerateObject())
        {
            if (!media.Value.TryGetProperty("schema", out var schema))
            {
                continue;
            }

            foreach (var propertyName in SchemaPropertyNames(schema, components, 0))
            {
                if (IsDirectionName(propertyName))
                {
                    offenders.Add($"{where}: body property '{propertyName}'");
                }
            }
        }
    }

    /// <summary>Property names of a schema, following local $ref links. Depth-limited against cycles.</summary>
    private static IEnumerable<string> SchemaPropertyNames(JsonElement schema, JsonElement components, int depth)
    {
        if (depth > 8 || schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (schema.TryGetProperty("$ref", out var reference) && reference.GetString() is { } target
            && target.StartsWith("#/components/schemas/", StringComparison.Ordinal)
            && components.ValueKind == JsonValueKind.Object
            && components.TryGetProperty("schemas", out var schemas)
            && schemas.TryGetProperty(target["#/components/schemas/".Length..], out var resolved))
        {
            foreach (var name in SchemaPropertyNames(resolved, components, depth + 1))
            {
                yield return name;
            }
        }

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                yield return property.Name;
            }
        }
    }

    /// <summary>Case-insensitive, ignoring '_' and '-', so "sort_order" and "Sort-Direction" are caught too.</summary>
    private static bool IsDirectionName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return DirectionNames.Contains(normalized);
    }
}
