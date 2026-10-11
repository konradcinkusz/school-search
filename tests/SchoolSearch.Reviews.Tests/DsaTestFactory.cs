using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.Dsa;

namespace SchoolSearch.Reviews.Tests;

/// <summary>
/// Hosts the Reviews service in Development with EF InMemory. Callers are identified by request headers
/// (X-Test-Sub, X-Test-Verified, X-Test-Roles) that the test event hook turns into a principal. The hook is
/// registered only here. Production validates tokens against authservice.
/// </summary>
public class DsaTestFactory : WebApplicationFactory<Program>
{
    public const string Pepper = "dsa-test-pepper";
    public const string ContactEmail = "kontakt@example.test";

    private readonly string _databaseName = "dsa-tests-" + Guid.NewGuid().ToString("N");
    private readonly string? _contactEmail;

    public DsaTestFactory()
        : this(ContactEmail)
    {
    }

    protected DsaTestFactory(string? contactEmail)
    {
        _contactEmail = contactEmail;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reviews:SubjectPepper"] = Pepper,
            ["Auth:Authority"] = "https://auth.test.invalid",
            ["Auth:Issuer"] = "test-issuer",
            ["Auth:Audience"] = "test-audience",
            ["Dsa:ContactEmail"] = _contactEmail,
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ReviewsDbContext>>();
            services.AddDbContext<ReviewsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.PostConfigureAll<JwtBearerOptions>(options =>
            {
                options.Events = new JwtBearerEvents { OnMessageReceived = HeaderPrincipal };
            });
        });
    }

    /// <summary>A client whose requests carry the given identity. Verified adds email_verified=true.</summary>
    public HttpClient CreateAs(string sub, bool verified = true, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Sub", sub);
        if (verified)
        {
            client.DefaultRequestHeaders.Add("X-Test-Verified", "true");
        }

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        }

        return client;
    }

    public static string HashSubject(string sub) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Pepper + sub))).ToLowerInvariant();

    public static JsonContent JsonBody(object body) => JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public async Task UseDbAsync(Func<ReviewsDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        await action(db);
    }

    public async Task<T> ReadDbAsync<T>(Func<ReviewsDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        return await query(db);
    }

    public const string ReviewBody = "Opinia testowa o szkole, wystarczajaco dluga do walidacji treci.";

    public static Review NewReview(string schoolSlug, string authorSub, string status = "pending") => new()
    {
        Id = Guid.NewGuid(),
        SchoolSlug = schoolSlug,
        AuthorHash = HashSubject(authorSub),
        Rating = 4,
        Title = "Tytul opinii",
        Body = ReviewBody,
        SchoolYearFrom = 2025,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(1, 10_000)),
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = status == "published" ? DateTimeOffset.UtcNow : null,
        TermsVersion = LegalDocuments.TermsVersion,
    };

    private static Task HeaderPrincipal(MessageReceivedContext context)
    {
        var headers = context.Request.Headers;
        var sub = headers["X-Test-Sub"].ToString();
        if (sub.Length == 0)
        {
            return Task.CompletedTask;
        }

        var claims = new List<Claim> { new Claim("sub", sub) };
        if (string.Equals(headers["X-Test-Verified"].ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("email_verified", "true", ClaimValueTypes.Boolean));
        }

        foreach (var role in headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("role", role));
        }

        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "DsaTest", "sub", "role"));
        context.Success();
        return Task.CompletedTask;
    }
}

/// <summary>Same host with no contact point configured.</summary>
public sealed class DsaTestFactoryNoContact : DsaTestFactory
{
    public DsaTestFactoryNoContact()
        : base(null)
    {
    }
}
