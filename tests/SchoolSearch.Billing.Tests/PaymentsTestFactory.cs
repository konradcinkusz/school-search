using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Tests;

/// <summary>
/// Hosts the real Billing service in-process for the Payments tests: InMemory database (unique per instance), the test
/// auth scheme, and the Billing settings the webhook path reads. Self-contained, so the Payments tests do not depend on
/// the shared core factory. The test scheme is registered here only.
/// </summary>
public sealed class PaymentsTestFactory : WebApplicationFactory<Program>
{
    public const string Secret = "payments-test-secret-not-real";
    public const string Pepper = "payments-test-pepper";
    public const string Issuer = "schoolsearch-billing-test";
    public const string Audience = "schoolsearch-billing-test-audience";

    private readonly bool _mockMode;
    private readonly string? _secret;
    private readonly string _environment;
    private readonly string _databaseName = "billing-payments-" + Guid.NewGuid().ToString("N");

    /// <param name="mockMode">Billing:MockMode.</param>
    /// <param name="secret">Billing:WebhookSecret. Null leaves it unset.</param>
    /// <param name="environment">Host environment. Production exercises the reject-unsigned rule.</param>
    public PaymentsTestFactory(bool mockMode = true, string? secret = Secret, string environment = "Testing")
    {
        _mockMode = mockMode;
        _secret = secret;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Billing:SubjectPepper", Pepper);
        builder.UseSetting("Billing:MockMode", _mockMode ? "true" : "false");
        if (_secret is not null)
        {
            builder.UseSetting("Billing:WebhookSecret", _secret);
        }

        builder.UseSetting("Billing:Plans:0:Code", "monthly");
        builder.UseSetting("Billing:Plans:0:Name", "Plan miesięczny (test)");
        builder.UseSetting("Billing:Plans:0:Interval", "month");
        builder.UseSetting("Billing:Plans:1:Code", "yearly");
        builder.UseSetting("Billing:Plans:1:Name", "Plan roczny (test)");
        builder.UseSetting("Billing:Plans:1:Interval", "year");

        builder.UseSetting("Auth:Authority", "https://authservice.test");
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);

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

            services.AddAuthentication(options => options.DefaultAuthenticateScheme = PaymentsTestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, PaymentsTestAuthHandler>(PaymentsTestAuthHandler.SchemeName, _ => { });
        });
    }

    // EF Core registers IDbContextOptionsConfiguration<TContext> next to the options. Matched by name, as in the Reviews factory.
    private static bool IsOptionsConfigurationFor<TContext>(Type serviceType) =>
        serviceType.IsGenericType
        && serviceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
        && serviceType.GenericTypeArguments[0] == typeof(TContext);

    public async Task UseDbAsync(Func<BillingDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        await action(db);
    }

    public async Task<T> QueryDbAsync<T>(Func<BillingDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.EnsureCreatedAsync();
        return await query(db);
    }

    /// <summary>The author key the service stores for a subject, computed with the server's own hasher.</summary>
    public string HashFor(string subject)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SubjectHasher>().Hash(subject);
    }
}

/// <summary>
/// Test-only scheme. X-Test-Sub (absent means anonymous) and X-Test-Email-Verified (true/false) become the sub and
/// email_verified claims that the production policies read. Never registered by production code.
/// </summary>
public sealed class PaymentsTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PaymentsTestBearer";
    public const string SubHeader = "X-Test-Sub";
    public const string EmailVerifiedHeader = "X-Test-Email-Verified";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new Claim(AuthClaims.SubjectClaim, sub.ToString()) };
        if (Request.Headers.TryGetValue(EmailVerifiedHeader, out var verified))
        {
            claims.Add(new Claim(AuthClaims.EmailVerifiedClaim, verified.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
