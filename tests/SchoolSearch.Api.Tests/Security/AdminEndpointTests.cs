using System.Net;
using System.Text;
using System.Text.Json;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// The admin group (ADR-0015): 404 when Auth:Authority is unset, 401 without a valid authservice bearer token,
/// 403 for a valid token without an Admin or SuperAdmin role, 200 for an admin. Tokens are test tokens (TestBearerAuthHandler).
/// </summary>
public sealed class AdminEndpointTests
{
    private const string AdminToken = "Bearer test:admin-user:Admin";
    private const string SuperAdminToken = "Bearer test:root-user:SuperAdmin";
    private const string MemberToken = "Bearer test:member-user:Member,User";

    private static readonly string ListPath = "/api/v1/admin/corrections";

    private static readonly IReadOnlyDictionary<string, string> AuthorityConfigured = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Auth:Authority"] = "https://auth.test.invalid",
        ["Auth:Issuer"] = "SchoolSearchTest",
        ["Auth:Audience"] = "school-search-test",
    };

    private static HttpRequestMessage Request(HttpMethod method, string path, string? authorization)
    {
        var request = new HttpRequestMessage(method, path);
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    private static HttpRequestMessage PatchCorrection(string id, string? authorization)
    {
        var request = Request(HttpMethod.Patch, $"/api/v1/admin/corrections/{id}", authorization);
        request.Content = new StringContent("{\"status\":\"accepted\"}", Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task Admin_routes_answer_404_when_Auth_Authority_is_unset_even_with_a_token()
    {
        using var factory = new ConfiguredApiFactory();
        var client = factory.CreateClient();
        var id = Guid.NewGuid().ToString();

        using var list = Request(HttpMethod.Get, ListPath, AdminToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(list), HttpStatusCode.NotFound, "not_found");

        using var patch = PatchCorrection(id, AdminToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(patch), HttpStatusCode.NotFound, "not_found");

        using var revoke = Request(HttpMethod.Post, $"/api/v1/admin/keys/{id}/revoke", AdminToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(revoke), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Missing_or_unreadable_token_is_a_uniform_401_with_identical_bodies_and_no_echo()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        var client = factory.CreateClient();

        using var missingRequest = Request(HttpMethod.Get, ListPath, null);
        var missingResponse = await client.SendAsync(missingRequest);
        var missing = await ContractAssert.ErrorResponseAsync(missingResponse, HttpStatusCode.Unauthorized, "unauthorized");

        using var wrongRequest = Request(HttpMethod.Get, ListPath, "Bearer not-a-readable-token");
        var wrongResponse = await client.SendAsync(wrongRequest);
        var wrong = await ContractAssert.ErrorResponseAsync(wrongResponse, HttpStatusCode.Unauthorized, "unauthorized");

        Assert.Equal(missing.GetProperty("error").GetProperty("message").GetString(), wrong.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("not-a-readable-token", wrong.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Bearer", missingResponse.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Authenticated_user_without_an_admin_role_is_403_on_every_admin_write()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        var client = factory.CreateClient();
        var id = Guid.NewGuid().ToString();

        using var list = Request(HttpMethod.Get, ListPath, MemberToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(list), HttpStatusCode.Forbidden, "forbidden");

        using var patch = PatchCorrection(id, MemberToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(patch), HttpStatusCode.Forbidden, "forbidden");

        using var revoke = Request(HttpMethod.Post, $"/api/v1/admin/keys/{id}/revoke", MemberToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(revoke), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Admin_token_without_a_subject_is_refused()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        using var request = Request(HttpMethod.Get, ListPath, "Bearer test::Admin");

        await ContractAssert.ErrorResponseAsync(await factory.CreateClient().SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
    }

    [Theory]
    [InlineData("Bearer test:admin-user:Admin")]
    [InlineData("Bearer test:root-user:SuperAdmin")]
    [InlineData("Bearer testlong:admin-user:Admin")]
    public async Task Admin_or_superadmin_role_opens_the_correction_list_under_either_role_claim_name(string authorization)
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        using var request = Request(HttpMethod.Get, ListPath, authorization);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "items", "page", "pageSize", "total");
    }

    [Fact]
    public async Task A_valid_admin_token_reaches_the_correction_handler()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        var client = factory.CreateClient();

        // The id does not exist: a 404 from the handler (not from the filter) proves the token passed the gate.
        var missingCorrection = Guid.NewGuid().ToString();
        using var patch = PatchCorrection(missingCorrection, SuperAdminToken);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(patch), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Former_admin_key_header_and_token_in_the_query_string_are_not_accepted()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        var client = factory.CreateClient();

        using var oldHeader = Request(HttpMethod.Get, ListPath, null);
        oldHeader.Headers.TryAddWithoutValidation("X-Admin-Key", "replace-me-test-admin-credential");
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(oldHeader), HttpStatusCode.Unauthorized, "unauthorized");

        var inQuery = await client.GetAsync($"{ListPath}?access_token=admin-user&adminKey=x");
        await ContractAssert.ErrorResponseAsync(inQuery, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Public_api_key_is_not_an_admin_credential()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        var client = factory.CreateClient();
        var issued = await JsonTestHelpers.ReadJsonAsync(await client.PostAsync(
            "/api/v1/keys", new StringContent("{\"label\":\"not-admin\"}", Encoding.UTF8, "application/json")));
        var publicKey = issued.GetProperty("apiKey").GetString() ?? string.Empty;

        using var request = Request(HttpMethod.Get, ListPath, "Bearer " + publicKey);
        await ContractAssert.ErrorResponseAsync(await client.SendAsync(request), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Admin_responses_are_never_cached()
    {
        using var factory = new ConfiguredApiFactory(AuthorityConfigured);
        using var request = Request(HttpMethod.Get, ListPath, AdminToken);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.True(response.Headers.CacheControl?.NoStore ?? false, "Admin responses must send Cache-Control: no-store.");
    }

    [Fact]
    public async Task Health_reports_authservice_disabled_without_Authority_and_ok_with_it()
    {
        using var unset = new ConfiguredApiFactory();
        var unsetRoot = await JsonTestHelpers.ReadJsonAsync(await unset.CreateClient().GetAsync("/health"));
        AssertIntegrationState(unsetRoot, "authservice", "disabled");

        using var set = new ConfiguredApiFactory(AuthorityConfigured);
        var setRoot = await JsonTestHelpers.ReadJsonAsync(await set.CreateClient().GetAsync("/health"));
        AssertIntegrationState(setRoot, "authservice", "ok");
    }

    [Fact]
    public void Authority_without_issuer_or_audience_fails_startup_and_names_the_setting()
    {
        var incomplete = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Auth:Authority"] = "https://auth.test.invalid",
        };
        using var factory = new ConfiguredApiFactory(incomplete);

        var error = Assert.ThrowsAny<Exception>(() => { factory.CreateClient(); });

        Assert.Contains("Auth:Issuer", FlattenMessages(error), StringComparison.Ordinal);
    }

    private static void AssertIntegrationState(JsonElement root, string integrationName, string expectedState)
    {
        var entry = JsonTestHelpers.FindObjectContainingString(root, integrationName);
        Assert.True(entry.HasValue, $"/health does not list integration '{integrationName}'. Body: {root.GetRawText()}");
        Assert.True(JsonTestHelpers.ContainsString(entry.Value, expectedState),
            $"Integration '{integrationName}' is not reported with state '{expectedState}'. Entry: {entry.Value.GetRawText()}");
    }

    private static string FlattenMessages(Exception? error)
    {
        var messages = new StringBuilder();
        for (var current = error; current is not null; current = current.InnerException)
        {
            messages.AppendLine(current.Message);
        }

        return messages.ToString();
    }
}
