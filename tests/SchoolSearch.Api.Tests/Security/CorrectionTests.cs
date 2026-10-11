using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Catalog.Data;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// Correction requests: the token is the only handle (>= 22 url-safe characters, 128 random bits), unknown and
/// malformed tokens share one 404, the internal id is never a lookup key, and message and email are never echoed.
/// </summary>
public sealed class CorrectionTests
{
    private const string Message = "Synthetic reporter text: the listed address of this school is out of date.";
    private const string Email = "synthetic-reporter@example.com";

    private static readonly Regex TokenShape = new("^[A-Za-z0-9_-]{22,}$", RegexOptions.CultureInvariant);

    private static StringContent Submission(string message, string? email = Email, string slug = PublicSeed.SchoolA) =>
        new(JsonSerializer.Serialize(new { slug, field = "name", message, contactEmail = email }), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Create_returns_201_with_only_the_token_and_status_and_never_echoes_message_or_email()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync("/api/v1/corrections", Submission(Message));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "token", "status");
        Assert.Equal("new", root.GetProperty("status").GetString());
        Assert.Matches(TokenShape, root.GetProperty("token").GetString() ?? string.Empty);

        var raw = root.GetRawText();
        Assert.DoesNotContain(Message, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tokens_are_random_and_never_sequential_numbers()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var first = (await JsonTestHelpers.ReadJsonAsync(await client.PostAsync("/api/v1/corrections", Submission(Message))))
            .GetProperty("token").GetString() ?? string.Empty;
        var second = (await JsonTestHelpers.ReadJsonAsync(await client.PostAsync("/api/v1/corrections", Submission(Message + " two"))))
            .GetProperty("token").GetString() ?? string.Empty;

        Assert.NotEqual(first, second);
        Assert.False(long.TryParse(first, out _), "A token must never be a plain number.");
        Assert.False(Guid.TryParse(first, out _), "A token must never be a GUID.");
    }

    [Fact]
    public async Task Status_lookup_returns_only_status_fields_and_no_message_or_email()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();
        var token = (await JsonTestHelpers.ReadJsonAsync(await client.PostAsync("/api/v1/corrections", Submission(Message))))
            .GetProperty("token").GetString() ?? string.Empty;

        var response = await client.GetAsync($"/api/v1/corrections/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "token", "status", "statusReason", "createdAt", "schoolSlug", "field", "kind");
        Assert.Equal(token, root.GetProperty("token").GetString());
        Assert.Equal("new", root.GetProperty("status").GetString());
        Assert.Equal(PublicSeed.SchoolA, root.GetProperty("schoolSlug").GetString());
        Assert.Equal("name", root.GetProperty("field").GetString());
        Assert.Equal("school", root.GetProperty("kind").GetString());

        var raw = root.GetRawText();
        Assert.DoesNotContain(Message, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_and_malformed_tokens_get_the_same_uniform_404()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        // Well-formed but never issued, a sequential-looking value, and a value with an invalid character.
        var unknown = await ContractAssert.ErrorResponseAsync(
            await client.GetAsync("/api/v1/corrections/AAAAAAAAAAAAAAAAAAAAAA"), HttpStatusCode.NotFound, "not_found");
        var sequential = await ContractAssert.ErrorResponseAsync(
            await client.GetAsync("/api/v1/corrections/1"), HttpStatusCode.NotFound, "not_found");
        var invalid = await ContractAssert.ErrorResponseAsync(
            await client.GetAsync("/api/v1/corrections/bad%20token%21"), HttpStatusCode.NotFound, "not_found");

        var message = unknown.GetProperty("error").GetProperty("message").GetString();
        Assert.Equal(message, sequential.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(message, invalid.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_internal_guid_is_not_a_lookup_key()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(async db =>
        {
            await PublicSeed.ApplyAsync(db);
            db.CorrectionRequests.Add(new SchoolSearch.Catalog.Data.Entities.CorrectionRequest
            {
                Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                PublicToken = "Abcdefghijklmnopqrstuv", // exactly 22 characters, the shape the handler accepts
                SchoolId = 1,
                Field = "name",
                Message = Message,
                Status = "new",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        });
        var client = factory.CreateClient();

        // The row exists and is reachable by its token, but its internal Guid is not a handle.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/corrections/Abcdefghijklmnopqrstuv")).StatusCode);
        await ContractAssert.ErrorResponseAsync(
            await client.GetAsync("/api/v1/corrections/11111111-2222-3333-4444-555555555555"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Body_over_16_KB_is_refused_and_nothing_is_stored()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var oversize = new string('x', 20_000);
        var response = await factory.CreateClient().PostAsync("/api/v1/corrections", Submission(oversize));

        Assert.True(
            response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest,
            $"Expected 413 or 400, got {(int)response.StatusCode}.");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Empty(await db.CorrectionRequests.ToListAsync());
    }

    [Fact]
    public async Task Field_outside_the_allowlist_and_a_malformed_email_are_400()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var badField = new StringContent(
            JsonSerializer.Serialize(new { slug = PublicSeed.SchoolA, field = "password", message = Message }),
            Encoding.UTF8,
            "application/json");
        await ContractAssert.ErrorResponseAsync(await client.PostAsync("/api/v1/corrections", badField), HttpStatusCode.BadRequest, "validation_failed");

        var badEmail = await client.PostAsync("/api/v1/corrections", Submission(Message, "not-an-address"));
        await ContractAssert.ErrorResponseAsync(badEmail, HttpStatusCode.BadRequest, "validation_failed");
    }
}
