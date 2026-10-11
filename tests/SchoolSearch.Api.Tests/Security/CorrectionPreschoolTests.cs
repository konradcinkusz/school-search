using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Api.Corrections;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// Correction requests about a preschool (kind "preschool"): its own field allowlist, the same token and flood rules,
/// and a storage encoding that can never attribute a preschool correction to a school with the same SIO id.
/// </summary>
public sealed class CorrectionPreschoolTests
{
    private const string PreschoolSlug = "przedszkole-korekta-testowa";
    private const string Message = "Synthetic reporter text: the tuition figure on this preschool page is out of date.";

    [Fact]
    public async Task Preschool_correction_is_accepted_and_its_status_names_the_preschool_and_the_bare_field()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var created = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "tuition_min", message = Message }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = (await JsonTestHelpers.ReadJsonAsync(created)).GetProperty("token").GetString() ?? string.Empty;

        using var status = await client.GetAsync($"/api/v1/corrections/{token}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(status);
        Assert.Equal(PreschoolSlug, root.GetProperty("schoolSlug").GetString());
        Assert.Equal("tuition_min", root.GetProperty("field").GetString());
        Assert.Equal("preschool", root.GetProperty("kind").GetString());
        Assert.DoesNotContain(Message, root.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stored_preschool_row_never_shares_a_school_id_and_is_marked_by_its_field_prefix()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var created = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "hours", message = Message }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var row = await db.CorrectionRequests.AsNoTracking().SingleAsync();

        // The seeded preschool has IdPodmiot 1, and school 1 exists too. The stored id is negative, so no school matches it.
        Assert.Equal("preschool.hours", row.Field);
        Assert.Equal(-1L, row.SchoolId);
        Assert.Equal(0, await db.Schools.CountAsync(s => s.IdPodmiot == row.SchoolId));
    }

    [Fact]
    public async Task School_correction_with_the_same_numeric_id_still_resolves_to_the_school()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var school = await client.PostAsync("/api/v1/corrections", Body(new { slug = PublicSeed.SchoolA, field = "address", message = Message }));
        Assert.Equal(HttpStatusCode.Created, school.StatusCode);
        var token = (await JsonTestHelpers.ReadJsonAsync(school)).GetProperty("token").GetString() ?? string.Empty;

        using var status = await client.GetAsync($"/api/v1/corrections/{token}");
        var root = await JsonTestHelpers.ReadJsonAsync(status);
        Assert.Equal(PublicSeed.SchoolA, root.GetProperty("schoolSlug").GetString());
        Assert.Equal("school", root.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_school_field_is_refused_for_a_preschool_and_a_preschool_field_is_refused_for_a_school()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var schoolField = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "name", message = Message }));
        await ContractAssert.ErrorResponseAsync(schoolField, HttpStatusCode.BadRequest, "validation_failed");

        using var preschoolField = await client.PostAsync("/api/v1/corrections", Body(new { slug = PublicSeed.SchoolA, field = "tuition_min", message = Message }));
        await ContractAssert.ErrorResponseAsync(preschoolField, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Unknown_kind_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/corrections", Body(new { kind = "hospital", slug = PreschoolSlug, field = "other", message = Message }));

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_school_slug_sent_as_a_preschool_is_not_found()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PublicSeed.SchoolA, field = "other", message = Message }));

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task The_flood_limit_applies_to_preschool_corrections_per_field()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < CorrectionEndpoints.MaxOpenPerSchoolFieldPerClient; i++)
        {
            using var accepted = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "free_places", message = Message + " " + i }));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var limited = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "free_places", message = Message + " over" }));
        await ContractAssert.ErrorResponseAsync(limited, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task Preschool_and_school_corrections_on_the_same_field_do_not_share_a_flood_allowance()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < CorrectionEndpoints.MaxOpenPerSchoolFieldPerClient; i++)
        {
            using var accepted = await client.PostAsync("/api/v1/corrections", Body(new { kind = "preschool", slug = PreschoolSlug, field = "address", message = Message + " " + i }));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        // The preschool allowance is used up; the school allowance for the same field code is untouched.
        using var school = await client.PostAsync("/api/v1/corrections", Body(new { slug = PublicSeed.SchoolA, field = "address", message = Message }));
        Assert.Equal(HttpStatusCode.Created, school.StatusCode);
    }

    private static async Task<ConfiguredApiFactory> SeededFactoryAsync()
    {
        var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(async db =>
        {
            await PublicSeed.ApplyAsync(db);
            db.Preschools.Add(new Preschool
            {
                IdPodmiot = 1,
                Slug = PreschoolSlug,
                Name = "Przedszkole testowe korekty",
                GminaTeryt = "1111111",
                GminaName = "Gmina testowa",
                IsPublic = true,
                Provenance = new Provenance
                {
                    Source = "Synthetic register row",
                    SourceUrl = "https://example.invalid/synthetic",
                    AsOf = new DateOnly(2025, 6, 30),
                    Method = "synthetic",
                },
            });
        });
        return factory;
    }

    private static StringContent Body(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
