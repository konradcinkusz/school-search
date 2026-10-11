using System.Net;
using System.Text;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>The admin group: 404 when ADMIN_API_KEY is unset, a uniform 401 without the right X-Admin-Key header, 200 with it.</summary>
public sealed class AdminEndpointTests
{
    private const string ConfigurationKey = "ADMIN_API_KEY";
    private const string HeaderName = "X-Admin-Key";
    private const string AdminKey = "replace-me-test-admin-credential";

    private static readonly string ListPath = "/api/v1/admin/corrections";

    private static Dictionary<string, string> WithAdminKey() =>
        new(StringComparer.Ordinal) { [ConfigurationKey] = AdminKey };

    private static HttpRequestMessage Request(HttpMethod method, string path, string? headerValue)
    {
        var request = new HttpRequestMessage(method, path);
        if (headerValue is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderName, headerValue);
        }

        return request;
    }

    [Fact]
    public async Task Admin_routes_answer_404_when_ADMIN_API_KEY_is_unset_even_with_a_header_present()
    {
        using var factory = new ConfiguredApiFactory();
        var client = factory.CreateClient();
        var id = Guid.NewGuid().ToString();

        using var list = Request(HttpMethod.Get, ListPath, "anything");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(list), HttpStatusCode.NotFound, "not_found");

        using var patch = Request(HttpMethod.Patch, $"/api/v1/admin/corrections/{id}", "anything");
        patch.Content = new StringContent("{\"status\":\"accepted\"}", Encoding.UTF8, "application/json");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(patch), HttpStatusCode.NotFound, "not_found");

        using var revoke = Request(HttpMethod.Post, $"/api/v1/admin/keys/{id}/revoke", "anything");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(revoke), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Missing_or_wrong_admin_key_is_a_uniform_401_with_identical_bodies_and_no_echo()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        var client = factory.CreateClient();

        using var missingRequest = Request(HttpMethod.Get, ListPath, null);
        var missing = await ContractAssert.ErrorResponseAsync(await client.SendAsync(missingRequest), HttpStatusCode.Unauthorized, "unauthorized");

        using var wrongRequest = Request(HttpMethod.Get, ListPath, "not-the-admin-key");
        var wrong = await ContractAssert.ErrorResponseAsync(await client.SendAsync(wrongRequest), HttpStatusCode.Unauthorized, "unauthorized");

        Assert.Equal(missing.GetProperty("error").GetProperty("message").GetString(), wrong.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(AdminKey, missing.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-admin-key", wrong.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_key_is_refused_on_every_admin_write_before_the_body_is_read()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        var client = factory.CreateClient();
        var id = Guid.NewGuid().ToString();

        using var patch = Request(HttpMethod.Patch, $"/api/v1/admin/corrections/{id}", "wrong");
        patch.Content = new StringContent("{\"status\":\"accepted\"}", Encoding.UTF8, "application/json");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(patch), HttpStatusCode.Unauthorized, "unauthorized");

        using var revoke = Request(HttpMethod.Post, $"/api/v1/admin/keys/{id}/revoke", "wrong");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(revoke), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Correct_admin_key_opens_the_correction_list()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        using var request = Request(HttpMethod.Get, ListPath, AdminKey);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "items", "page", "pageSize", "total");
    }

    [Fact]
    public async Task Admin_key_passed_in_the_query_string_is_not_accepted()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        var client = factory.CreateClient();

        var byName = await client.GetAsync($"{ListPath}?X-Admin-Key={AdminKey}");
        await ContractAssert.ErrorResponseAsync(byName, HttpStatusCode.Unauthorized, "unauthorized");

        var byParameter = await client.GetAsync($"{ListPath}?adminKey={AdminKey}");
        await ContractAssert.ErrorResponseAsync(byParameter, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Public_api_key_is_not_an_admin_credential()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        var client = factory.CreateClient();
        var issued = await JsonTestHelpers.ReadJsonAsync(await client.PostAsync(
            "/api/v1/keys", new StringContent("{\"label\":\"not-admin\"}", Encoding.UTF8, "application/json")));
        var publicKey = issued.GetProperty("apiKey").GetString() ?? string.Empty;

        using var request = Request(HttpMethod.Get, ListPath, publicKey);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(request), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Admin_responses_are_never_cached()
    {
        using var factory = new ConfiguredApiFactory(WithAdminKey());
        using var request = Request(HttpMethod.Get, ListPath, AdminKey);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.True(response.Headers.CacheControl?.NoStore ?? false, "Admin responses must send Cache-Control: no-store.");
    }
}
