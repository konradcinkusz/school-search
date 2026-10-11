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
/// Hosts the real Api with a fake model client. The fake replaces the registered <see cref="IExplainClient"/>, so no
/// test touches the network. The key is set to a placeholder, or to an empty value to test the degraded path.
/// Each instance gets its own InMemory catalog.
/// </summary>
public sealed class AiApiFactory : WebApplicationFactory<Program>
{
    private const string PlaceholderKey = "test-key-placeholder";

    private readonly string _databaseName = "catalog-ai-" + Guid.NewGuid().ToString("N");
    private readonly bool _withKey;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public AiApiFactory(
        FakeExplainClient? client = null,
        bool withKey = true,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        Client = client ?? new FakeExplainClient();
        _withKey = withKey;
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public FakeExplainClient Client { get; }

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

            services.RemoveAll<IExplainClient>();
            services.AddSingleton<IExplainClient>(Client);
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in TestSupport.cs.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    /// <summary>Creates the schema and runs <paramref name="seed"/> against this factory's catalog.</summary>
    public async Task SeedAsync(Func<CatalogDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }
}
