using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

/// <summary>Orchestration through the real HttpSeedTransport, with every service replaced by a FakeSeedServer.</summary>
public sealed class SeedRunnerTests
{
    private const string GrantRolePath = "POST auth.test/api/v1/admin/users/sub-moderator/roles";
    private const string LoginKey = "POST auth.test/api/v1/auth/login";

    [Fact]
    public async Task Happy_path_seeds_everything_and_prints_no_secrets()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        string text = built.Output.ToString();
        Assert.Contains("reviews /dev/seed: reviews=8, notices=3, ownerReplies=1, triage=2, alreadySeeded=false", text);
        Assert.Contains("billing /dev/seed: claims=2, subscriptions=1, profiles=1, enquiries=4, alreadySeeded=false", text);
        Assert.Contains("Seeding complete.", text);
        Assert.Contains("moderator@school-search.test", text);
        Assert.DoesNotContain(SeedFixtures.TestPassword, text);
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, text);
        Assert.DoesNotContain("eyJ", text);
    }

    [Fact]
    public async Task Moderator_is_granted_admin_with_the_contract_body()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        RecordedRequest grant = server.Requests.Single(r => r.Key == GrantRolePath);
        Assert.Equal("{\"role\":\"Admin\"}", grant.Body);
        Assert.NotNull(grant.Authorization);
    }

    [Fact]
    public async Task Dev_seeds_get_the_key_header_and_the_subject_sets()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        RecordedRequest reviews = server.Requests.Single(r => r.Key == "POST reviews.test/dev/seed");
        Assert.Equal(SeedFixtures.DevSeedKey, reviews.DevSeedKey);
        Assert.Equal(new[] { "moderator", "owner", "parent", "parent2" }, SubjectKeys(reviews.Body));

        RecordedRequest billing = server.Requests.Single(r => r.Key == "POST billing.test/dev/seed");
        Assert.Equal(SeedFixtures.DevSeedKey, billing.DevSeedKey);
        Assert.Equal(new[] { "moderator", "owner" }, SubjectKeys(billing.Body));
    }

    [Fact]
    public async Task Existing_test_account_is_signed_in_instead_of_failing()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("POST auth.test/api/v1/auth/register", request =>
        {
            string requested = SeedFixtures.FieldOf(request.Body, "email");
            return requested == "parent@school-search.test"
                ? new Reply(400, "{\"errors\":[\"Email 'parent@school-search.test' is already taken.\"]}")
                : new Reply(200, SeedFixtures.TokenBody(SeedFixtures.SubjectFor(requested)));
        });
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Contains(
            server.Requests,
            r => r.Key == LoginKey && SeedFixtures.FieldOf(r.Body, "email") == "parent@school-search.test");
        Assert.Contains("parent@school-search.test: already exists, signed in.", built.Output.ToString());
        Assert.Contains("Seeding complete.", built.Output.ToString());
    }

    [Fact]
    public async Task Conflict_status_on_register_also_falls_back_to_sign_in()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("POST auth.test/api/v1/auth/register", new Reply(409, "{\"error\":\"exists\"}"));
        server.Route(LoginKey, request => new Reply(200, SeedFixtures.TokenBody(SeedFixtures.SubjectFor(SeedFixtures.FieldOf(request.Body, "email")))));
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Contains("already exists, signed in.", built.Output.ToString());
    }

    [Fact]
    public async Task Rate_limited_sign_in_waits_for_retry_after_and_then_succeeds()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        bool limited = false;
        server.Route(LoginKey, request =>
        {
            if (!limited && SeedFixtures.FieldOf(request.Body, "email") == SeedFixtures.AdminEmail)
            {
                limited = true;
                return new Reply(429, "{\"error\":\"Too many requests. Please try again later.\",\"retryAfter\":1}", "1");
            }

            return new Reply(200, SeedFixtures.TokenBody(SeedFixtures.SubjectFor(SeedFixtures.FieldOf(request.Body, "email"))));
        });
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, built.Clock.Delays);
        Assert.Equal(2, server.Requests.Count(r => r.Key == LoginKey && SeedFixtures.FieldOf(r.Body, "email") == SeedFixtures.AdminEmail));
        Assert.Contains("Seeding complete.", built.Output.ToString());
    }

    [Fact]
    public async Task Rate_limit_without_a_hint_uses_the_backoff_schedule()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        int calls = 0;
        server.Route(LoginKey, request =>
        {
            calls++;
            if (calls == 1)
            {
                return new Reply(429, "{}");
            }

            return new Reply(200, SeedFixtures.TokenBody(SeedFixtures.SubjectFor(SeedFixtures.FieldOf(request.Body, "email"))));
        });
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, built.Clock.Delays);
    }

    [Fact]
    public async Task Service_that_never_becomes_ready_fails_at_the_three_minute_budget()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("GET auth.test/health/ready", new Reply(503, "{}"));
        var built = SeedFixtures.BuildRunner(server);

        SeedFailureException ex = await Assert.ThrowsAsync<SeedFailureException>(
            () => built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None));

        Assert.Contains("authservice was not ready within 3 minutes", ex.Message);
        Assert.Equal(TimeSpan.FromMinutes(3), built.Clock.Now);
        Assert.DoesNotContain(server.Requests, r => r.Key == LoginKey);
    }

    [Fact]
    public async Task Wrong_admin_password_fails_without_echoing_the_password()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route(LoginKey, new Reply(401, "{\"error\":\"Invalid credentials.\"}"));
        var built = SeedFixtures.BuildRunner(server);

        SeedFailureException ex = await Assert.ThrowsAsync<SeedFailureException>(
            () => built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None));

        Assert.Contains("Admin sign-in failed", ex.Message);
        Assert.DoesNotContain(SeedFixtures.TestPassword, ex.Message);
    }

    [Fact]
    public async Task Reviews_rejecting_the_dev_seed_key_fails_the_run_before_billing()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("POST reviews.test/dev/seed", new Reply(401, "{\"error\":\"Invalid dev seed key.\"}"));
        var built = SeedFixtures.BuildRunner(server);

        SeedFailureException ex = await Assert.ThrowsAsync<SeedFailureException>(
            () => built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None));

        Assert.Contains("reviews rejected the dev seed key", ex.Message);
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, ex.Message);
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, built.Output.ToString());
        Assert.DoesNotContain(server.Requests, r => r.Key == "POST billing.test/dev/seed");
    }

    [Fact]
    public async Task Role_already_present_is_not_granted_again()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route(
            "GET auth.test/api/v1/admin/users/sub-moderator",
            new Reply(200, "{\"id\":\"sub-moderator\",\"roles\":[\"User\",\"Admin\"]}"));
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.DoesNotContain(server.Requests, r => r.Key == GrantRolePath);
        Assert.Contains("already has role Admin.", built.Output.ToString());
    }

    [Fact]
    public async Task Role_grant_answering_already_has_role_is_tolerated()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route(GrantRolePath, new Reply(400, "{\"error\":\"User already has role 'Admin'\"}"));
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Contains("already has role Admin.", built.Output.ToString());
        Assert.Contains("Seeding complete.", built.Output.ToString());
    }

    [Fact]
    public async Task Role_grant_with_an_unexpected_error_fails_the_run()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route(GrantRolePath, new Reply(403, "{\"error\":\"Forbidden\"}"));
        var built = SeedFixtures.BuildRunner(server);

        SeedFailureException ex = await Assert.ThrowsAsync<SeedFailureException>(
            () => built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None));

        Assert.Contains("Assigning role Admin to moderator@school-search.test failed", ex.Message);
    }

    [Fact]
    public async Task Missing_consent_versions_fall_back_to_the_contract_default()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("GET auth.test/api/v1/auth/consents/versions", new Reply(500, "{}"));
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options(), CancellationToken.None);

        Assert.Contains("using 2026-01-01", built.Output.ToString());
        RecordedRequest register = server.Requests.First(r => r.Key == "POST auth.test/api/v1/auth/register");
        Assert.Contains("\"acceptedTermsVersion\":\"2026-01-01\"", register.Body);
    }

    [Fact]
    public async Task Generated_password_is_printed_in_the_summary()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options("true"), CancellationToken.None);

        string text = built.Output.ToString();
        Assert.Contains(SeedFixtures.TestPassword, text);
        Assert.Contains("per-run local password", text);
    }

    [Fact]
    public async Task Operator_set_password_is_not_printed_in_the_summary()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        var built = SeedFixtures.BuildRunner(server);

        await built.Runner.RunAsync(SeedFixtures.Options("false"), CancellationToken.None);

        Assert.DoesNotContain(SeedFixtures.TestPassword, built.Output.ToString());
    }

    private static string[] SubjectKeys(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("subjects")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>The exit-code contract of the console entry point, end to end through the fake server.</summary>
public sealed class SeederAppTests
{
    [Fact]
    public async Task Full_run_exits_zero_with_no_error_output()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = await SeederApp.RunAsync(
            SeedFixtures.Lookup(SeedFixtures.ValidEnvironment()),
            output,
            error,
            SeedFixtures.HappyServer(),
            CancellationToken.None);

        Assert.Equal(SeederApp.ExitOk, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Seeding complete.", output.ToString());
    }

    [Fact]
    public async Task Reviews_401_exits_one_with_a_clear_message()
    {
        FakeSeedServer server = SeedFixtures.HappyServer();
        server.Route("POST reviews.test/dev/seed", new Reply(401, "{\"error\":\"Invalid dev seed key.\"}"));
        var output = new StringWriter();
        var error = new StringWriter();

        int code = await SeederApp.RunAsync(
            SeedFixtures.Lookup(SeedFixtures.ValidEnvironment()),
            output,
            error,
            server,
            CancellationToken.None);

        Assert.Equal(SeederApp.ExitSeedFailed, code);
        Assert.Contains("rejected the dev seed key", error.ToString());
        Assert.DoesNotContain(SeedFixtures.DevSeedKey, error.ToString());
    }

    [Fact]
    public async Task Missing_configuration_exits_two_and_names_the_variable()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int code = await SeederApp.RunAsync(
            SeedFixtures.Lookup(new Dictionary<string, string>(StringComparer.Ordinal)),
            output,
            error,
            null,
            CancellationToken.None);

        Assert.Equal(SeederApp.ExitConfiguration, code);
        Assert.Contains("AUTH_BASE_URL is required", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }
}
