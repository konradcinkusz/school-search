using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

/// <summary>A canned answer from the fake server.</summary>
internal sealed record Reply(int Status, string Body, string? RetryAfter = null);

/// <summary>One request as the fake server saw it. Authorization carries a token and is never printed by the tests.</summary>
internal sealed record RecordedRequest(string Key, string Body, string? Authorization, string? DevSeedKey);

/// <summary>
/// A fake HttpMessageHandler. Routes are keyed "METHOD host/path" (for example "POST reviews.test/dev/seed"), so the
/// three services can share paths such as /health. Unscripted requests get a 404 and are still recorded.
/// </summary>
internal sealed class FakeSeedServer : HttpMessageHandler
{
    private readonly Dictionary<string, Func<RecordedRequest, Reply>> _routes = new(StringComparer.Ordinal);

    public List<RecordedRequest> Requests { get; } = new();

    public void Route(string key, Reply reply)
    {
        _routes[key] = _ => reply;
    }

    public void Route(string key, Func<RecordedRequest, Reply> handler)
    {
        _routes[key] = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI.");
        string key = request.Method.Method + " " + uri.Host + uri.AbsolutePath;
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        string? devSeedKey = null;
        if (request.Headers.TryGetValues(SeedRequests.DevSeedKeyHeader, out IEnumerable<string>? values))
        {
            devSeedKey = values?.FirstOrDefault();
        }

        var recorded = new RecordedRequest(key, body, request.Headers.Authorization?.Parameter, devSeedKey);
        Requests.Add(recorded);

        Reply reply = _routes.TryGetValue(key, out Func<RecordedRequest, Reply>? route)
            ? route(recorded)
            : new Reply(404, "{\"error\":\"not scripted\"}");

        var response = new HttpResponseMessage((HttpStatusCode)reply.Status)
        {
            Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
        };
        if (reply.RetryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", reply.RetryAfter);
        }

        return response;
    }
}

/// <summary>Virtual time for backoff and readiness tests: a delay advances the clock and returns at once.</summary>
internal sealed class VirtualClock
{
    public TimeSpan Now { get; private set; }

    public List<TimeSpan> Delays { get; } = new();

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        Now += delay;
        return Task.CompletedTask;
    }
}

/// <summary>Builds unsigned JWTs with the shape the seeder reads. The signature segment is a placeholder.</summary>
internal static class FakeJwt
{
    public static string For(string subject)
    {
        return WithPayload("{\"sub\":" + JsonSerializer.Serialize(subject) + "}");
    }

    public static string WithPayload(string payloadJson)
    {
        return Encode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}") + "." + Encode(payloadJson) + ".signature";
    }

    private static string Encode(string json)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>Shared test data and the happy-path fake of the three services.</summary>
internal static class SeedFixtures
{
    public const string AdminEmail = "admin@school-search.test";
    public const string TestPassword = "pw-test-only-123";
    public const string DevSeedKey = "key-123";

    public static string SubjectFor(string email) => "sub-" + email.Split('@')[0];

    public static string TokenBody(string subject)
    {
        return "{\"accessToken\":\"" + FakeJwt.For(subject) + "\",\"refreshToken\":\"refresh-token\",\"expiresIn\":3600}";
    }

    public static string FieldOf(string json, string name)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetString() ?? string.Empty;
    }

    public static Dictionary<string, string> ValidEnvironment(string? passwordGenerated = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUTH_BASE_URL"] = "http://auth.test",
            ["REVIEWS_BASE_URL"] = "http://reviews.test/",
            ["BILLING_BASE_URL"] = "http://billing.test",
            ["DEV_SEED_KEY"] = DevSeedKey,
            ["SEED_ADMIN_EMAIL"] = AdminEmail,
            ["SEED_TEST_PASSWORD"] = TestPassword,
        };
        if (passwordGenerated is not null)
        {
            values["SEED_PASSWORD_GENERATED"] = passwordGenerated;
        }

        return values;
    }

    public static Func<string, string?> Lookup(IReadOnlyDictionary<string, string> values)
    {
        return name => values.TryGetValue(name, out string? value) ? value : null;
    }

    public static SeederOptions Options(string? passwordGenerated = null)
    {
        return SeederOptions.FromEnvironment(Lookup(ValidEnvironment(passwordGenerated)));
    }

    /// <summary>A server where every call succeeds on the first try.</summary>
    public static FakeSeedServer HappyServer()
    {
        var server = new FakeSeedServer();
        server.Route("GET auth.test/health/ready", new Reply(200, "{\"status\":\"Healthy\"}"));
        server.Route("GET reviews.test/health", new Reply(200, "{\"status\":\"Healthy\"}"));
        server.Route("GET billing.test/health", new Reply(200, "{\"status\":\"Healthy\"}"));
        server.Route(
            "GET auth.test/api/v1/auth/consents/versions",
            new Reply(200, "{\"terms\":\"2026-01-01\",\"privacy\":\"2026-01-01\",\"cookies\":\"2026-01-01\"}"));
        server.Route(
            "POST auth.test/api/v1/auth/login",
            request => new Reply(200, TokenBody(SubjectFor(FieldOf(request.Body, "email")))));
        server.Route(
            "POST auth.test/api/v1/auth/register",
            request => new Reply(200, TokenBody(SubjectFor(FieldOf(request.Body, "email")))));
        server.Route(
            "GET auth.test/api/v1/admin/users/sub-moderator",
            new Reply(200, "{\"id\":\"sub-moderator\",\"roles\":[\"User\"]}"));
        server.Route(
            "POST auth.test/api/v1/admin/users/sub-moderator/roles",
            new Reply(200, "{\"message\":\"Role 'Admin' assigned to user sub-moderator\"}"));
        server.Route(
            "POST reviews.test/dev/seed",
            new Reply(200, "{\"reviews\":8,\"notices\":3,\"ownerReplies\":1,\"triage\":2,\"alreadySeeded\":false}"));
        server.Route(
            "POST billing.test/dev/seed",
            new Reply(200, "{\"claims\":2,\"subscriptions\":1,\"profiles\":1,\"enquiries\":4,\"alreadySeeded\":false}"));
        return server;
    }

    public static (SeedRunner Runner, StringWriter Output, VirtualClock Clock) BuildRunner(FakeSeedServer server)
    {
        var clock = new VirtualClock();
        var output = new StringWriter();
        var runner = new SeedRunner(
            new HttpSeedTransport(new HttpClient(server)),
            output,
            clock.Delay,
            () => clock.Now);
        return (runner, output, clock);
    }
}
