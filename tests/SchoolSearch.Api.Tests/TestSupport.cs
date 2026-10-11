using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Catalog.Data;
using Xunit.Sdk;

namespace SchoolSearch.Api.Tests;

/// <summary>
/// Hosts the real Api in-process. The InMemory database name is unique per factory instance,
/// so every test class instance (xUnit builds a new one per test) gets a fresh database.
/// </summary>
public sealed class SchoolSearchApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "catalog-test-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>)
                         || d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task SeedSchoolsAsync(int count)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.EnsureCreatedAsync();
        for (var i = 1; i <= count; i++)
        {
            db.Schools.Add(new School
            {
                IdPodmiot = i,
                Name = $"Szkola testowa {i}",
                Slug = $"szkola-testowa-{i}",
            });
        }

        await db.SaveChangesAsync();
    }
}

internal static class JsonTestHelpers
{
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Counts the items of a page. Accepts either a bare array or an object whose first array
    /// property holds the items; fails the test if neither shape is present.
    /// </summary>
    public static int CountPageItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.GetArrayLength();
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value.GetArrayLength();
            }
        }

        throw new XunitException("Response contains no items array: " + root.GetRawText());
    }

    /// <summary>
    /// Finds the JSON object that carries a string value equal to <paramref name="marker"/>
    /// anywhere in the document, whatever its surrounding shape is.
    /// </summary>
    public static JsonElement? FindObjectContainingString(JsonElement element, string marker)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == marker)
                {
                    return element;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindObjectContainingString(property.Value, marker);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindObjectContainingString(item, marker);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    public static bool ContainsString(JsonElement element, string value)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() == value,
            JsonValueKind.Object => element.EnumerateObject().Any(p => ContainsString(p.Value, value)),
            JsonValueKind.Array => element.EnumerateArray().Any(e => ContainsString(e, value)),
            _ => false,
        };
    }
}
