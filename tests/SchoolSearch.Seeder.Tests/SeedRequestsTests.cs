using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

public sealed class SeedRequestsTests
{
    private static readonly IReadOnlyDictionary<string, string> Subjects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["parent"] = "sub-parent",
        ["owner"] = "sub-owner",
    };

    [Fact]
    public async Task Dev_seed_carries_the_key_in_the_header_only()
    {
        using HttpRequestMessage request = SeedRequests.DevSeed(new Uri("http://reviews.test/dev/seed"), SeedFixtures.DevSeedKey, Subjects);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SeedFixtures.DevSeedKey, request.Headers.GetValues(SeedRequests.DevSeedKeyHeader).Single());
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, request.RequestUri!.AbsoluteUri);
        Assert.NotNull(request.Content);
        string body = await request.Content!.ReadAsStringAsync();
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, body);
    }

    [Fact]
    public async Task Dev_seed_body_has_the_subjects_object()
    {
        using HttpRequestMessage request = SeedRequests.DevSeed(new Uri("http://billing.test/dev/seed"), SeedFixtures.DevSeedKey, Subjects);
        string body = await request.Content!.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement subjects = document.RootElement.GetProperty("subjects");
        Assert.Equal("sub-parent", subjects.GetProperty("parent").GetString());
        Assert.Equal("sub-owner", subjects.GetProperty("owner").GetString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void Assign_role_body_is_exactly_the_role_object()
    {
        string json = SeedRequests.AssignRole("Admin").ToJsonString();

        Assert.Equal("{\"role\":\"Admin\"}", json);
    }

    [Fact]
    public async Task Register_body_carries_the_consent_versions()
    {
        using HttpRequestMessage request = SeedRequests.Post(
            new Uri("http://auth.test/api/v1/auth/register"),
            SeedRequests.Register("moderator@school-search.test", "pw-test-only-123", "2026-01-01", "2027-02-02"));
        string body = await request.Content!.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        Assert.Equal("moderator@school-search.test", root.GetProperty("email").GetString());
        Assert.Equal("pw-test-only-123", root.GetProperty("password").GetString());
        Assert.Equal("2026-01-01", root.GetProperty("acceptedTermsVersion").GetString());
        Assert.Equal("2027-02-02", root.GetProperty("acceptedPrivacyVersion").GetString());
    }

    [Fact]
    public void Login_body_carries_email_and_password()
    {
        string json = SeedRequests.Login("parent@school-search.test", "pw").ToJsonString();

        Assert.Equal("{\"email\":\"parent@school-search.test\",\"password\":\"pw\"}", json);
    }

    [Fact]
    public void Get_with_a_token_sets_a_bearer_authorization_header()
    {
        using HttpRequestMessage request = SeedRequests.Get(new Uri("http://auth.test/api/v1/admin/users/sub-1"), "token-value");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-value", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void Get_without_a_token_sets_no_authorization_header()
    {
        using HttpRequestMessage request = SeedRequests.Get(new Uri("http://auth.test/health/ready"));

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public void Post_with_a_token_sets_a_bearer_authorization_header()
    {
        using HttpRequestMessage request = SeedRequests.Post(
            new Uri("http://auth.test/api/v1/admin/users/sub-1/roles"),
            SeedRequests.AssignRole("Admin"),
            "token-value");

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("token-value", request.Headers.Authorization!.Parameter);
    }
}
