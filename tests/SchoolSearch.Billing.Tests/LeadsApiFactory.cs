using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.Tests;

/// <summary>
/// Hosts the real Billing service in-process with an InMemory database (unique per factory instance) and the Leads test
/// auth scheme. Private to the Leads tests: it does not depend on F1's factory. Each test uses its own slug and client IP,
/// so the shared database and the per-IP windows do not couple tests to each other.
/// </summary>
public sealed class LeadsApiFactory : WebApplicationFactory<Program>
{
    public const string TestPepper = "leads-test-pepper-not-a-secret";
    public const string BffSecret = "leads-test-bff-secret-not-a-secret";
    public const string Issuer = "schoolsearch-billing-leads-test";
    public const string Audience = "schoolsearch-billing-leads-test-audience";

    private readonly string _databaseName = "billing-leads-test-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Billing:SubjectPepper", TestPepper);
        builder.UseSetting("BFF_SHARED_SECRET", BffSecret);
        builder.UseSetting("Auth:Authority", "https://authservice.test");
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            var registrations = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<BillingDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || IsOptionsConfigurationFor(d.ServiceType))
                .ToList();
            foreach (var registration in registrations)
            {
                services.Remove(registration);
            }

            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // The test scheme becomes the default, so the policies read test headers and no discovery or JWKS is needed.
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = LeadsTestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LeadsTestAuthHandler>(LeadsTestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options; matched by name, as in the Reviews factory.
    private static bool IsOptionsConfigurationFor(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(BillingDbContext);

    /// <summary>The owner key the service stores for a subject, computed with the test pepper.</summary>
    public static string OwnerHashFor(string subject) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestPepper + subject))).ToLowerInvariant();

    /// <summary>
    /// Seeds an approved claim, a subscription and a profile for a school, bypassing the endpoints. Defaults give an
    /// open premium school; the flags produce the closed states.
    /// </summary>
    public async Task SeedPremiumSchoolAsync(
        string slug,
        string ownerSub,
        DateTimeOffset periodEnd,
        bool profileLocked = false,
        string subscriptionStatus = "active",
        string claimStatus = "approved")
    {
        var now = DateTimeOffset.UtcNow;
        var hash = OwnerHashFor(ownerSub);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.SchoolClaims.Add(new SchoolClaim
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            OwnerHash = hash,
            RoleDescription = "Dyrekcja",
            Status = claimStatus,
            CreatedAt = now,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            OwnerHash = hash,
            PlanCode = "test-plan",
            Status = subscriptionStatus,
            CurrentPeriodEnd = periodEnd,
            CancelAtPeriodEnd = false,
            ProviderRef = null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.PremiumProfiles.Add(new PremiumProfile
        {
            SchoolSlug = slug,
            Headline = "Nagłówek testowy",
            Description = "Opis testowy.",
            WebsiteUrl = null,
            IsLocked = profileLocked,
            LockedReason = profileLocked ? "subscription_expired" : null,
            LockedAt = profileLocked ? now : null,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds one enquiry directly, with explicit timestamps.</summary>
    public async Task<Guid> SeedLeadAsync(string slug, DateTimeOffset createdAt, DateTimeOffset expiresAt, string status = "new")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            ParentEmail = "rodzic@example.test",
            Message = "Wiadomość testowa do szkoły.",
            ConsentVersion = LeadConsent.Version,
            ConsentAt = createdAt,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            Status = status,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    public async Task<List<Lead>> ListLeadsAsync(string slug)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await db.Leads.AsNoTracking().Where(l => l.SchoolSlug == slug).ToListAsync();
    }

    public async Task<Lead?> FindLeadAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
    }

    /// <summary>Runs the purge against the test database, as the hosted service does.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset now)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        return await LeadPurgeService.PurgeExpiredAsync(db, now, CancellationToken.None);
    }
}
