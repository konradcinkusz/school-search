using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Settings;
using static SchoolSearch.Reviews.Tests.JsonTestHelpers;

namespace SchoolSearch.Reviews.Tests;

public sealed class HealthAndConfigurationTests
{
    [Fact]
    public async Task Health_reports_authservice_ok_and_a_configured_pepper_when_configured()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var states = IntegrationStates(await ReadJsonAsync(response));
        Assert.Equal("ok", states["authservice"]);
        Assert.Equal("ok", states["reviews-pepper"]);
    }

    [Fact]
    public async Task Health_reports_authservice_disabled_and_still_answers_200_when_not_configured()
    {
        using var factory = new ReviewsApiFactory(authConfigured: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("disabled", IntegrationStates(await ReadJsonAsync(response))["authservice"]);
    }

    [Fact]
    public async Task Moderator_policy_accepts_admin_and_super_admin_and_rejects_ordinary_users()
    {
        using var factory = new ReviewsApiFactory();
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(Principal("u1", roles: "Admin"), null, AuthPolicies.Moderator)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal("u2", roles: "SuperAdmin"), null, AuthPolicies.Moderator)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal("u3", roles: "User"), null, AuthPolicies.Moderator)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal("u4"), null, AuthPolicies.Moderator)).Succeeded);
    }

    [Fact]
    public async Task Verified_user_policy_needs_a_subject_and_a_verified_e_mail()
    {
        using var factory = new ReviewsApiFactory();
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(Principal("u1", verified: true), null, AuthPolicies.VerifiedUser)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principal("u1", verified: false), null, AuthPolicies.VerifiedUser)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principal("u1", verified: false), null, AuthPolicies.Authenticated)).Succeeded);
    }

    [Fact]
    public void Subject_hash_is_64_lowercase_hex_and_depends_on_the_pepper()
    {
        var environment = new TestHostEnvironment(Environments.Development);
        var first = new SubjectHasher(Microsoft.Extensions.Options.Options.Create(new ReviewsOptions { SubjectPepper = "pepper-one" }), environment);
        var second = new SubjectHasher(Microsoft.Extensions.Options.Options.Create(new ReviewsOptions { SubjectPepper = "pepper-two" }), environment);

        var hash = first.Hash("sub-123");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, first.Hash("sub-123"));
        Assert.NotEqual(hash, second.Hash("sub-123"));
        Assert.False(first.IsEphemeral);
    }

    [Fact]
    public void Outside_production_without_a_pepper_an_ephemeral_pepper_is_used()
    {
        var environment = new TestHostEnvironment(Environments.Staging);
        var hasher = new SubjectHasher(Microsoft.Extensions.Options.Options.Create(new ReviewsOptions()), environment);

        Assert.True(hasher.IsConfigured);
        Assert.True(hasher.IsEphemeral);
    }

    [Fact]
    public void Production_without_a_pepper_is_not_configured_so_review_writes_answer_503()
    {
        var environment = new TestHostEnvironment(Environments.Production);
        var hasher = new SubjectHasher(Microsoft.Extensions.Options.Options.Create(new ReviewsOptions()), environment);

        Assert.False(hasher.IsConfigured);
    }

    [Fact]
    public void The_authservice_default_issuer_and_audience_are_refused()
    {
        var defaults = new AuthServiceOptions { Authority = "https://auth.example", Issuer = "AuthService", Audience = "reviews" };
        var product = new AuthServiceOptions { Authority = "https://auth.example", Issuer = "SchoolSearch", Audience = "reviews" };
        var noAuthority = new AuthServiceOptions { Issuer = "SchoolSearch", Audience = "reviews" };

        Assert.False(defaults.IsConfigured);
        Assert.True(product.IsConfigured);
        Assert.False(noAuthority.IsConfigured);
    }

    private static Dictionary<string, string> IntegrationStates(System.Text.Json.JsonElement health) =>
        health.GetProperty("integrations").EnumerateArray()
            .ToDictionary(
                integration => integration.GetProperty("name").GetString()!,
                integration => integration.GetProperty("state").GetString()!);

    private static ClaimsPrincipal Principal(string subject, bool verified = true, string? roles = null)
    {
        var claims = new List<Claim>
        {
            new Claim(AuthClaims.SubjectClaim, subject),
            new Claim(AuthClaims.EmailVerifiedClaim, verified ? "true" : "false"),
        };

        foreach (var role in (roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            claims.Add(new Claim(AuthClaims.ShortRoleClaim, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}

/// <summary>Minimal <see cref="IHostEnvironment"/> for constructing services directly in tests.</summary>
internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "SchoolSearch.Reviews.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
