using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Subscriptions;

namespace SchoolSearch.Billing.Tests;

/// <summary>
/// Hosts the real Billing service in-process with an InMemory database (unique per factory instance), mock mode on,
/// one test plan configured, and the test auth scheme. The test scheme is registered here only.
/// authConfigured = false is the P8 degraded mode (503 auth_unavailable); withPlans = false leaves Billing:Plans empty.
/// </summary>
public sealed class BillingApiFactory : WebApplicationFactory<Program>
{
    private readonly bool authConfigured;
    private readonly bool withPlans;

    /// <summary>The only public constructor, so xUnit can build the class fixture.</summary>
    public BillingApiFactory() : this(true, true)
    {
    }

    internal BillingApiFactory(bool authConfigured, bool withPlans)
    {
        this.authConfigured = authConfigured;
        this.withPlans = withPlans;
    }

    public const string TestPepper = "test-pepper-not-a-secret";
    public const string Issuer = "schoolsearch-billing-test";
    public const string Audience = "schoolsearch-billing-test-audience";
    public const string PlanCode = "test-plan";
    public const string CheckoutBase = "https://billing.test";

    private readonly string _databaseName = "billing-test-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Billing:SubjectPepper", TestPepper);
        builder.UseSetting("Billing:MockMode", "true");
        builder.UseSetting("Billing:CheckoutBaseUrl", CheckoutBase);
        builder.UseSetting("Billing:ExpirationIntervalMinutes", "60");

        if (withPlans)
        {
            builder.UseSetting("Billing:Plans:0:Code", PlanCode);
            builder.UseSetting("Billing:Plans:0:Name", "Plan testowy");
            builder.UseSetting("Billing:Plans:0:Interval", "month");
        }

        if (authConfigured)
        {
            builder.UseSetting("Auth:Authority", "https://authservice.test");
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
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

            // Test auth scheme, registered here only. It becomes the default authenticate scheme, so the policies read
            // test headers instead of validating authservice tokens (no discovery or JWKS in tests).
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in the Reviews factory.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    /// <summary>Inserts rows directly, bypassing the endpoints (approved claims, live subscriptions, locked profiles).</summary>
    public async Task SeedAsync(params object[] entities)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <summary>Runs one query against a fresh scope and returns its result.</summary>
    public async Task<T> QueryAsync<T>(Func<BillingDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
    }

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
