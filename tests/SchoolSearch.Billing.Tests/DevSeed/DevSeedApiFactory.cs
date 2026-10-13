using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.DevSeed;
using SchoolSearch.Billing.Subscriptions;

namespace SchoolSearch.Billing.Tests.DevSeed;

/// <summary>Row counts of the four tables the dev seed writes.</summary>
public sealed record RowCounts(int Claims, int Subscriptions, int Profiles, int Leads);

/// <summary>
/// Hosts the real Billing service in-process for the dev seed tests: InMemory store (unique per instance), mock mode, the
/// test auth scheme, and the DevSeed settings under test. enabled = null leaves DevSeed:Enabled unset (the default).
/// key = null leaves DevSeed:Key unset.
/// </summary>
public sealed class DevSeedApiFactory : WebApplicationFactory<Program>
{
    public const string Key = "devseed-test-key-not-a-secret";
    public const string Pepper = "devseed-test-pepper";
    public const string Issuer = "schoolsearch-billing-test";
    public const string Audience = "schoolsearch-billing-test-audience";

    private readonly bool? _enabled;
    private readonly string? _key;
    private readonly string _environment;
    private readonly string _databaseName = "billing-devseed-" + Guid.NewGuid().ToString("N");

    public DevSeedApiFactory(bool? enabled, string? key, string environment)
    {
        _enabled = enabled;
        _key = key;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Billing:SubjectPepper", Pepper);
        builder.UseSetting("Billing:MockMode", "true");
        builder.UseSetting("Auth:Authority", "https://authservice.test");
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);

        if (_enabled is { } enabled)
        {
            builder.UseSetting(DevSeedGate.EnabledKey, enabled ? "true" : "false");
        }

        if (_key is not null)
        {
            builder.UseSetting(DevSeedGate.KeyKey, _key);
        }

        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<BillingDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || IsOptionsConfigurationFor<BillingDbContext>(d.ServiceType))
                .ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // Test auth scheme, registered here only (no authservice discovery or JWKS in tests).
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in the other factories.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    /// <summary>Runs one query in a fresh scope.</summary>
    public async Task<T> QueryAsync<T>(Func<BillingDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
    }

    /// <summary>Row counts of the four tables the dev seed writes.</summary>
    public Task<RowCounts> CountRowsAsync() => QueryAsync(async db => new RowCounts(
        await db.SchoolClaims.CountAsync(),
        await db.Subscriptions.CountAsync(),
        await db.PremiumProfiles.CountAsync(),
        await db.Leads.CountAsync()));

    /// <summary>The owner key the service stores for a subject, with the test pepper.</summary>
    public string HashFor(string subject)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SubjectHasher>().Hash(subject);
    }

    /// <summary>Entitlement decision for a school at a given instant, through the real service.</summary>
    public async Task<bool> IsPremiumAsync(string slug, DateTimeOffset now)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EntitlementService>().IsPremiumAsync(slug, now);
    }
}
