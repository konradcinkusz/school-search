using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Api.Ai;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Hosts the real Api with a fake search-intent model. The fake replaces the registered <see cref="ISearchIntentClient"/>,
/// so no test touches the network. The key is a placeholder, or empty to test the degraded path. The feature never
/// reads the catalog, so the database is the InMemory provider and no data is seeded.
/// </summary>
public sealed class SearchIntentApiFactory : WebApplicationFactory<Program>
{
    private const string PlaceholderKey = "test-key-placeholder";

    private readonly bool _withKey;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public SearchIntentApiFactory(
        SearchIntentFakeClient? client = null,
        bool withKey = true,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        Client = client ?? new SearchIntentFakeClient();
        _withKey = withKey;
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public SearchIntentFakeClient Client { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        // An empty value is read as "no key", so an ambient ANTHROPIC_API_KEY cannot switch the feature on by accident.
        builder.UseSetting(AiServiceCollectionExtensions.ApiKeyVariable, _withKey ? PlaceholderKey : string.Empty);

        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISearchIntentClient>();
            services.AddSingleton<ISearchIntentClient>(Client);
        });
    }
}
