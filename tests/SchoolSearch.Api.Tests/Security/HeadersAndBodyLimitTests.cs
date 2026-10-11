using System.Net;
using System.Text;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>Response headers on JSON and error responses, the admin no-store rule, and the 16 KB request body cap.</summary>
public sealed class HeadersAndBodyLimitTests
{
    [Theory]
    [InlineData("/api/v1/meta")]
    [InlineData("/api/v1/schools/no-such-school")]
    [InlineData("/api/v1/does-not-exist")]
    public async Task Json_and_error_responses_carry_the_api_security_headers(string path)
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal("nosniff", FirstHeader(response, "X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"), "Referrer-Policy is missing.");
        Assert.True(response.Headers.Contains("Content-Security-Policy"), "Content-Security-Policy is missing.");

        // Not asserted yet: X-Frame-Options and Permissions-Policy are part of the SECURITY-REVIEW §4 set but the
        // API does not send them (SecurityHeadersMiddleware). Tracked as a finding in docs/security/SECURITY-REVIEW-PHASE-C.md.
    }

    [Fact]
    public async Task Admin_paths_send_no_store()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/admin/corrections");

        Assert.True(response.Headers.CacheControl?.NoStore ?? false, "Cache-Control: no-store is missing on /api/v1/admin.");
    }

    [Theory]
    [InlineData("/api/v1/keys")]
    [InlineData("/api/v1/corrections")]
    public async Task Request_body_over_16_KB_is_rejected_with_413_before_the_handler(string path)
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var body = new StringContent(new string('x', 17 * 1024), Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync(path, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.UniformError(root, "payload_too_large");
    }

    private static string? FirstHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
