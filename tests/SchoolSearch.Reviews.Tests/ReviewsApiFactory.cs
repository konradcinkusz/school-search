using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Tests;

/// <summary>
/// Hosts the real Reviews service in-process with an InMemory database (unique per factory instance) and the
/// test auth scheme. The test scheme is registered here only; production code never sees it.
/// authConfigured = false leaves Auth:* unset, which is the P8 degraded mode (503 auth_unavailable).
/// </summary>
public sealed class ReviewsApiFactory : WebApplicationFactory<Program>
{
    private readonly bool authConfigured;

    /// <summary>The only public constructor, so xUnit can build the class fixture.</summary>
    public ReviewsApiFactory() : this(true)
    {
    }

    internal ReviewsApiFactory(bool authConfigured)
    {
        this.authConfigured = authConfigured;
    }

    public const string TestPepper = "test-pepper-not-a-secret";
    public const string TermsVersion = "draft-1";
    public const string Issuer = "schoolsearch-reviews-test";
    public const string Audience = "schoolsearch-reviews-test-audience";

    private readonly string _databaseName = "reviews-test-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Reviews:SubjectPepper", TestPepper);
        builder.UseSetting("Reviews:TermsVersion", TermsVersion);

        if (authConfigured)
        {
            builder.UseSetting("Auth:Authority", "https://authservice.test");
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
        }

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

            services.AddDbContext<ReviewsDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // Test auth scheme, registered here only. It becomes the default authenticate scheme, so the policies read
            // test headers instead of validating authservice tokens (no discovery or JWKS in tests).
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in the Api factory.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    /// <summary>Inserts rows directly, bypassing the endpoints (for example, published or moderated states).</summary>
    public async Task SeedAsync(params Review[] reviews)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Reviews.AddRange(reviews);
        await db.SaveChangesAsync();
    }

    public async Task<Review?> FindReviewAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        return await db.Reviews.AsNoTracking().Include(r => r.Reply).FirstOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>The author key the service stores for a subject, with the test pepper.</summary>
    public string HashFor(string subject)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SubjectHasher>().Hash(subject);
    }
}
