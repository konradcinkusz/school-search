using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.ServiceDefaults;
using Xunit;

namespace SchoolSearch.Api.Tests;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly SchoolSearchApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_lists_ok_degraded_and_disabled_integrations_with_their_state_and_detail()
    {
        var registry = _factory.Services.GetRequiredService<IntegrationStatusRegistry>();
        registry.Register("test-ok-integration", "ok", "reachable");
        registry.Register("test-degraded-integration", "degraded", "API key missing - AI features off");
        registry.Register("test-disabled-integration", "disabled", "not configured");

        var response = await _factory.CreateClient().GetAsync("/health");

        // P8: a degraded optional integration must not fail the probe.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await JsonTestHelpers.ReadJsonAsync(response);

        AssertIntegrationState(root, "test-ok-integration", "ok");
        AssertIntegrationState(root, "test-degraded-integration", "degraded");
        AssertIntegrationState(root, "test-disabled-integration", "disabled");
        Assert.True(JsonTestHelpers.ContainsString(root, "API key missing - AI features off"),
            "The degraded integration's detail must be reported, not only its state.");
    }

    [Fact]
    public async Task Alive_endpoint_answers_200()
    {
        var response = await _factory.CreateClient().GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertIntegrationState(JsonElement root, string integrationName, string expectedState)
    {
        var entry = JsonTestHelpers.FindObjectContainingString(root, integrationName);
        Assert.True(entry.HasValue, $"/health does not list integration '{integrationName}'. Body: {root.GetRawText()}");
        Assert.True(JsonTestHelpers.ContainsString(entry.Value, expectedState),
            $"Integration '{integrationName}' is not reported with state '{expectedState}'. Entry: {entry.Value.GetRawText()}");
    }
}
