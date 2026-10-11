using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace SchoolSearch.Reviews.Tests;

/// <summary>
/// Two gap-closure rules. (1) A trusted service (valid X-Bff-Secret, no X-Client-Ip) has its own bucket: it never
/// spends a visitor's allowance and a visitor never spends the service's. (2) In Production on the InMemory store,
/// every write answers 503 storage_unavailable while reads and health keep working.
/// </summary>
public sealed class ServiceBucketAndStorageGuardTests
{
    private const string Secret = "shared-test-secret-not-real";
    private const string Visitor = "198.51.100.9";
    private const string PublicList = "/api/v1/schools/szkola-testowa/reviews";

    [Fact]
    public async Task Trusted_service_calls_are_limited_in_their_own_bucket()
    {
        using var factory = Build("Testing", perIpPerMinute: 2, trustedServicePerMinute: 3);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add(await SendAsync(client, PublicList, Secret, clientIp: null));
        }

        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[0]);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[1]);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[2]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }

    [Fact]
    public async Task Visitor_calls_and_trusted_service_calls_do_not_share_a_bucket()
    {
        using var factory = Build("Testing", perIpPerMinute: 2, trustedServicePerMinute: 3);
        using var client = factory.CreateClient();

        // The visitor spends its own two requests, then is limited.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, Secret, Visitor));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, Secret, Visitor));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, Secret, Visitor));

        // The trusted service still has its full allowance: the visitor did not spend it.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, Secret, clientIp: null));
    }

    [Fact]
    public async Task A_wrong_secret_without_a_visitor_address_is_not_a_trusted_service()
    {
        using var factory = Build("Testing", perIpPerMinute: 2, trustedServicePerMinute: 3);
        using var client = factory.CreateClient();

        // Falls back to the caller's own address bucket, which allows two. The third is limited, although the
        // trusted-service bucket would still allow three.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, "not-the-secret", clientIp: null));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, "not-the-secret", clientIp: null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync(client, PublicList, "not-the-secret", clientIp: null));
    }

    [Fact]
    public async Task In_production_on_the_in_memory_store_writes_answer_503_storage_unavailable()
    {
        using var factory = Build("Production", perIpPerMinute: 60, trustedServicePerMinute: 600);
        using var client = factory.CreateClient();

        using var write = new HttpRequestMessage(HttpMethod.Post, PublicList) { Content = new StringContent("{}") };
        using var response = await client.SendAsync(write);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("storage_unavailable", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task In_production_reads_and_health_keep_working_and_health_shows_the_database_degraded()
    {
        using var factory = Build("Production", perIpPerMinute: 60, trustedServicePerMinute: 600);
        using var client = factory.CreateClient();

        var read = await SendAsync(client, PublicList, secret: null, clientIp: null);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, read);

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var document = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        var database = document.RootElement.GetProperty("integrations").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "database");
        Assert.Equal("degraded", database.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Outside_production_the_in_memory_store_still_accepts_writes()
    {
        using var factory = Build("Testing", perIpPerMinute: 60, trustedServicePerMinute: 600);
        using var client = factory.CreateClient();

        using var write = new HttpRequestMessage(HttpMethod.Post, PublicList) { Content = new StringContent("{}") };
        using var response = await client.SendAsync(write);

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string path, string? secret, string? clientIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (secret is not null)
        {
            request.Headers.Add("X-Bff-Secret", secret);
        }

        if (clientIp is not null)
        {
            request.Headers.Add("X-Client-Ip", clientIp);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static SettingsFactory Build(string environment, int perIpPerMinute, int trustedServicePerMinute) =>
        new(environment, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BFF_SHARED_SECRET"] = Secret,
            ["Reviews:SubjectPepper"] = "test-pepper-not-a-secret",
            ["Reviews:RateLimiting:PerIpPerMinute"] = perIpPerMinute.ToString(CultureInfo.InvariantCulture),
            ["Reviews:RateLimiting:TrustedServicePerMinute"] = trustedServicePerMinute.ToString(CultureInfo.InvariantCulture),
        });
}

/// <summary>Hosts the real Reviews service with the given environment and settings. Production code is unchanged.</summary>
internal sealed class SettingsFactory(string environment, IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
