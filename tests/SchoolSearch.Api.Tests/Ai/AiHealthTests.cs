using System.Net;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>The "anthropic" integration on /health: ok with a key, disabled without one (P8).</summary>
public sealed class AiHealthTests
{
    [Fact]
    public async Task Anthropic_integration_is_ok_when_a_key_is_configured()
    {
        using var factory = new AiApiFactory(withKey: true);

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        AssertIntegration(root, "ok");
    }

    [Fact]
    public async Task Anthropic_integration_is_disabled_without_a_key_and_the_probe_still_passes()
    {
        using var factory = new AiApiFactory(withKey: false);

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        AssertIntegration(root, "disabled");
        Assert.True(JsonTestHelpers.ContainsString(root, "ANTHROPIC_API_KEY is not set; AI explain is disabled"));
    }

    private static void AssertIntegration(System.Text.Json.JsonElement root, string expectedState)
    {
        var entry = JsonTestHelpers.FindObjectContainingString(root, "anthropic");
        Assert.True(entry.HasValue, $"/health does not list integration 'anthropic'. Body: {root.GetRawText()}");
        Assert.True(JsonTestHelpers.ContainsString(entry.Value, expectedState),
            $"Integration 'anthropic' is not reported as '{expectedState}'. Entry: {entry.Value.GetRawText()}");
    }
}
