using System.Globalization;
using System.Net;
using System.Text;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>Rate limits: the anonymous window, the probe exemption, client-IP trust and the key-issuance policy.</summary>
public sealed class RateLimitTests
{
    private static Dictionary<string, string> Settings(params (string Key, int Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);

    [Fact]
    public async Task Exceeding_the_anonymous_bucket_returns_429_with_retry_after_and_the_uniform_error()
    {
        using var factory = new ConfiguredApiFactory(Settings(("SchoolSearch:RateLimiting:PerIpPerMinute", 3)));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/meta")).StatusCode);
        }

        var limited = await client.GetAsync("/api/v1/meta");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"), "A 429 must carry Retry-After.");
        var retryAfter = limited.Headers.GetValues("Retry-After").Single();
        Assert.True(int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 1,
            $"Retry-After must be a positive number of seconds, was '{retryAfter}'.");

        var root = await JsonTestHelpers.ReadJsonAsync(limited);
        ContractAssert.UniformError(root, "rate_limited");
    }

    [Fact]
    public async Task Health_and_liveness_probes_stay_reachable_once_the_bucket_is_exhausted()
    {
        using var factory = new ConfiguredApiFactory(Settings(("SchoolSearch:RateLimiting:PerIpPerMinute", 1)));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/meta")).StatusCode);

        // A 429 on a probe would take the machine out of rotation (RateLimitingExtensions).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Fly_Client_IP_header_is_ignored_when_no_proxy_is_configured()
    {
        // TrustProxyClientIpHeader defaults to false. Trusting a client-supplied header would let each visitor pick a bucket.
        using var factory = new ConfiguredApiFactory(Settings(("SchoolSearch:RateLimiting:PerIpPerMinute", 2)));
        var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
            request.Headers.TryAddWithoutValidation("Fly-Client-IP", "203.0.113." + (i + 10).ToString(CultureInfo.InvariantCulture));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }

        using var spoofed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/meta");
        spoofed.Headers.TryAddWithoutValidation("Fly-Client-IP", "198.51.100.99");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(spoofed)).StatusCode);
    }

    [Fact]
    public async Task Key_issuance_has_its_own_strict_per_ip_limit_with_the_uniform_429()
    {
        using var factory = new ConfiguredApiFactory(Settings(("SchoolSearch:Security:KeyIssuePerIpPerHour", 2)));
        var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var issued = await client.PostAsync("/api/v1/keys", new StringContent("{\"label\":\"rate-test\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        }

        var limited = await client.PostAsync("/api/v1/keys", new StringContent("{\"label\":\"rate-test\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        ContractAssert.UniformError(await JsonTestHelpers.ReadJsonAsync(limited), "rate_limited");
    }
}
