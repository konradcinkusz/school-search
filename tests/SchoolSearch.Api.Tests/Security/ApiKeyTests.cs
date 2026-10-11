using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Catalog.Data;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>X-Api-Key issuance: the key is shown once, only its SHA-256 digest is stored, unknown keys are anonymous.</summary>
public sealed class ApiKeyTests
{
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Issuance_returns_a_256_bit_key_once_in_the_contract_shape()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().PostAsync("/api/v1/keys", Json("{\"label\":\"integration-test\"}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "apiKey");
        // "ss_" prefix plus 32 random bytes in unpadded base64url (43 characters).
        Assert.Matches("^ss_[A-Za-z0-9_-]{43}$", root.GetProperty("apiKey").GetString() ?? string.Empty);
    }

    [Fact]
    public async Task Only_the_sha256_digest_is_stored_and_no_column_of_the_stored_row_holds_the_key()
    {
        using var factory = new ConfiguredApiFactory();
        var root = await JsonTestHelpers.ReadJsonAsync(
            await factory.CreateClient().PostAsync("/api/v1/keys", Json("{\"label\":\"db-check\"}")));
        var key = root.GetProperty("apiKey").GetString() ?? string.Empty;
        Assert.False(string.IsNullOrEmpty(key));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var rows = await db.ApiKeys.ToListAsync();

        var row = Assert.Single(rows);
        var expectedDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        Assert.Equal(expectedDigest, row.KeyHash);

        // Every mapped column of the row, read through EF metadata, is checked for the plain key.
        foreach (var property in db.Entry(row).Properties)
        {
            var value = property.CurrentValue;
            Assert.False(
                value is string text && text.Contains(key, StringComparison.Ordinal),
                $"Column '{property.Metadata.Name}' holds the plain API key.");
        }
    }

    [Fact]
    public async Task A_valid_key_is_accepted_in_the_header_and_an_unknown_key_is_treated_as_anonymous()
    {
        using var factory = new ConfiguredApiFactory();
        var client = factory.CreateClient();
        var root = await JsonTestHelpers.ReadJsonAsync(await client.PostAsync("/api/v1/keys", Json("{\"label\":\"header-check\"}")));
        var key = root.GetProperty("apiKey").GetString() ?? string.Empty;

        using var valid = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        valid.Headers.TryAddWithoutValidation("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(valid)).StatusCode);

        using var unknown = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        unknown.Headers.TryAddWithoutValidation("X-Api-Key", "ss_thisKeyWasNeverIssued0000000000000000000");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(unknown)).StatusCode);

        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        malformed.Headers.TryAddWithoutValidation("X-Api-Key", "x");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(malformed)).StatusCode);
    }

    [Fact]
    public async Task Issuance_rejects_an_empty_or_control_character_label_with_the_uniform_400()
    {
        using var factory = new ConfiguredApiFactory();
        var client = factory.CreateClient();

        var empty = await client.PostAsync("/api/v1/keys", Json("{\"label\":\"   \"}"));
        await ContractAssert.ErrorResponseAsync(empty, HttpStatusCode.BadRequest);

        var control = await client.PostAsync("/api/v1/keys", Json("{\"label\":\"a\\u0007b\"}"));
        await ContractAssert.ErrorResponseAsync(control, HttpStatusCode.BadRequest);
    }
}
