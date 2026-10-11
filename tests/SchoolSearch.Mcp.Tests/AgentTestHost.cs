using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Mcp.Agent;
using SchoolSearch.Mcp.Catalog;
using SchoolSearch.Mcp.Reviews;

namespace SchoolSearch.Mcp.Tests;

/// <summary>A running agent host on an in-memory test server. Disposing it stops the host.</summary>
internal sealed class AgentHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;

    public WebApplication App { get; } = app;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

/// <summary>
/// Hosts only the agent routes, with the real AddAgent wiring, the real tool clients over a stub handler and no
/// Program.cs. The configuration is supplied in memory, so the machine's own environment cannot leak a key in.
/// </summary>
internal static class AgentTestHost
{
    /// <param name="apiKey">Empty for the "no key" state.</param>
    /// <param name="llm">A fake model, registered before AddAgent so the TryAdd keeps it. Null leaves the SDK path unregistered.</param>
    /// <param name="settings">Extra flat configuration values, such as AI_DAILY_TOKEN_BUDGET.</param>
    public static async Task<AgentHost> StartAsync(
        string apiKey,
        IAgentLlm? llm,
        AgentStubHandler stub,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });

        var values = new Dictionary<string, string?>
        {
            [AgentServiceCollectionExtensions.ApiKeyVariable] = apiKey,
            [AgentServiceCollectionExtensions.EnabledVariable] = "true",
            [AgentServiceCollectionExtensions.DailyBudgetVariable] = "200000",
            [AgentServiceCollectionExtensions.RequestsPerMinuteVariable] = "100",
            ["CATALOG_API_URL"] = AgentTestData.CatalogBase,
            ["REVIEWS_BASE_URL"] = AgentTestData.ReviewsBase,
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                values[pair.Key] = pair.Value;
            }
        }

        builder.Configuration.AddInMemoryCollection(values);
        builder.WebHost.UseTestServer();

        builder.Services.AddCatalogClient(builder.Configuration);
        builder.Services.AddReviewsClient(builder.Configuration);
        builder.Services.AddHttpClient(CatalogClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub);
        builder.Services.AddHttpClient(ReviewsClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub);

        if (llm is not null)
        {
            builder.Services.AddSingleton<IAgentLlm>(llm);
        }

        builder.Services.AddAgent(builder.Configuration);

        var app = builder.Build();
        app.MapAgentEndpoints();
        await app.StartAsync();

        return new AgentHost(app, app.GetTestClient());
    }
}
