using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Tests;

/// <summary>Row counts of every reviews table, to prove that a repeat seed writes nothing.</summary>
public sealed record TableCounts(int Reviews, int Replies, int Notices, int Decisions, int Complaints, int Triage);

/// <summary>
/// Hosts the Reviews service with the dev seed configured per test: enabled flag, key and environment. Same InMemory store
/// and test auth scheme as <see cref="ReviewsApiFactory"/>. Create one per test: each instance has its own database.
/// </summary>
public sealed class DevSeedFactory : WebApplicationFactory<Program>
{
    /// <summary>The key the tests send when the seed is enabled.</summary>
    public const string ConfiguredKey = "test-dev-seed-key-0123456789abcdef";

    private readonly bool enabled;
    private readonly string seedKey;
    private readonly string environmentName;
    private readonly string databaseName = "devseed-test-" + Guid.NewGuid().ToString("N");

    /// <summary>Enabled with the test key outside Production.</summary>
    public DevSeedFactory() : this(true, ConfiguredKey, "Testing")
    {
    }

    internal DevSeedFactory(bool enabled, string seedKey, string environmentName)
    {
        this.enabled = enabled;
        this.seedKey = seedKey;
        this.environmentName = environmentName;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Reviews:SubjectPepper", ReviewsApiFactory.TestPepper);
        builder.UseSetting("Reviews:TermsVersion", ReviewsApiFactory.TermsVersion);
        builder.UseSetting("Auth:Authority", "https://authservice.test");
        builder.UseSetting("Auth:Issuer", ReviewsApiFactory.Issuer);
        builder.UseSetting("Auth:Audience", ReviewsApiFactory.Audience);
        builder.UseSetting("DevSeed:Enabled", enabled ? "true" : "false");
        builder.UseSetting("DevSeed:Key", seedKey);

        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ReviewsDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || IsOptionsConfigurationFor<ReviewsDbContext>(d.ServiceType))
                .ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<ReviewsDbContext>(options => options.UseInMemoryDatabase(databaseName));

            // Test auth scheme, as in ReviewsApiFactory. Production code never sees it.
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in ReviewsApiFactory.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    /// <summary>Runs a read against the store the host is using.</summary>
    public async Task<T> WithDbAsync<T>(Func<ReviewsDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        return await action(db);
    }

    public async Task<TableCounts> CountAllAsync() => await WithDbAsync(async db => new TableCounts(
        await db.Reviews.CountAsync(),
        await db.ReviewReplies.CountAsync(),
        await db.Notices.CountAsync(),
        await db.ModerationDecisions.CountAsync(),
        await db.Complaints.CountAsync(),
        await db.ModerationTriages.CountAsync()));

    /// <summary>The author key the service stores for a subject, with the test pepper.</summary>
    public string HashFor(string subject)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SubjectHasher>().Hash(subject);
    }
}
