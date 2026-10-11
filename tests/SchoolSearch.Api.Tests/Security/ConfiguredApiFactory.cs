using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Tests.Security;

/// <summary>
/// Hosts the real Api in-process with its own InMemory catalog and configuration overrides.
/// It repeats the database swap of <c>SchoolSearchApiFactory</c> (TestSupport.cs), which is sealed and
/// takes no settings, so every factory here gets a fresh database and the same options can be overridden.
/// </summary>
public sealed class ConfiguredApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "catalog-cfg-" + Guid.NewGuid().ToString("N");
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly string _environment;

    /// <param name="settings">Configuration keys to set, for example <c>SchoolSearch:RateLimiting:PerIpPerMinute</c>.</param>
    /// <param name="environment">Hosting environment. Defaults to Testing, as the other Api tests do.</param>
    public ConfiguredApiFactory(IReadOnlyDictionary<string, string>? settings = null, string environment = "Testing")
    {
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");

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
