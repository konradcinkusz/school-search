using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Api.Ai;
using SchoolSearch.Api.Tests.Security;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Hosts the real Api with the admin gate (Auth settings plus the test bearer scheme, as in ConfiguredApiFactory) and a
/// fake triage client. No test touches the network. An empty key means the degraded path. Each instance gets its own
/// InMemory catalog.
/// </summary>
public sealed class CorrectionTriageApiFactory : WebApplicationFactory<Program>
{
    private const string PlaceholderKey = "test-key-placeholder";

    private readonly string _databaseName = "catalog-triage-" + Guid.NewGuid().ToString("N");
    private readonly bool _withKey;
    private readonly bool _authorityConfigured;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public CorrectionTriageApiFactory(
        FakeCorrectionTriageClient? client = null,
        bool withKey = true,
        bool authorityConfigured = true,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        Client = client ?? new FakeCorrectionTriageClient();
        _withKey = withKey;
        _authorityConfigured = authorityConfigured;
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public FakeCorrectionTriageClient Client { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        // An empty value is read as "no key", so an ambient ANTHROPIC_API_KEY cannot switch the feature on by accident.
        builder.UseSetting(AiServiceCollectionExtensions.ApiKeyVariable, _withKey ? PlaceholderKey : string.Empty);

        if (_authorityConfigured)
        {
            builder.UseSetting("Auth:Authority", "https://auth.test.invalid");
            builder.UseSetting("Auth:Issuer", "SchoolSearchTest");
            builder.UseSetting("Auth:Audience", "school-search-test");
        }

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

            services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestBearerAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestBearerAuthHandler>(TestBearerAuthHandler.SchemeName, _ => { });

            services.RemoveAll<ICorrectionTriageClient>();
            services.AddSingleton<ICorrectionTriageClient>(Client);
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
