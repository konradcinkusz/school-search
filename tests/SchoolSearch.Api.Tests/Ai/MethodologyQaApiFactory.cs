using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Api.Ai;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Hosts the real Api with a fake methodology model. The fake replaces the registered <see cref="IMethodologyQaClient"/>,
/// so no test touches the network. The key is a placeholder, or empty to test the degraded path.
/// </summary>
public sealed class MethodologyQaApiFactory : WebApplicationFactory<Program>
{
    private const string PlaceholderKey = "test-key-placeholder";

    private readonly string _databaseName = "catalog-methodology-" + Guid.NewGuid().ToString("N");
    private readonly bool _withKey;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public MethodologyQaApiFactory(
        FakeMethodologyQaClient? client = null,
        bool withKey = true,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        Client = client ?? new FakeMethodologyQaClient();
        _withKey = withKey;
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public FakeMethodologyQaClient Client { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting(AiServiceCollectionExtensions.ApiKeyVariable, _withKey ? PlaceholderKey : string.Empty);

        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || IsOptionsConfigurationFor<CatalogDbContext>(d.ServiceType))
                .ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IMethodologyQaClient>();
            services.AddSingleton<IMethodologyQaClient>(Client);
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in AiApiFactory.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);
}
