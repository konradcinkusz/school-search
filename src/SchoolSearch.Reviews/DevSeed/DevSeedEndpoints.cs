using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Errors;
using SchoolSearch.Reviews.Security;

namespace SchoolSearch.Reviews.DevSeed;

/// <summary>Body of POST /dev/seed. Every subject is optional.</summary>
public sealed class DevSeedRequest
{
    public DevSeedSubjectsRequest? Subjects { get; set; }
}

public sealed class DevSeedSubjectsRequest
{
    public string? Parent { get; set; }

    public string? Parent2 { get; set; }

    public string? Owner { get; set; }

    public string? Moderator { get; set; }
}

/// <summary>Response of POST /dev/seed.</summary>
public sealed record DevSeedResponse(int Reviews, int Notices, int OwnerReplies, int Triage, bool AlreadySeeded);

/// <summary>
/// POST /dev/seed. The route is mapped only when DevSeed:Enabled is true, DevSeed:Key is set and the environment is not
/// Production. Otherwise no route exists and the service answers 404. The header X-Dev-Seed-Key is compared in constant time.
/// </summary>
/// <remarks>
/// Never enable this outside local development. The demo acknowledgement tokens written by the seed are fixed and therefore
/// guessable, so anyone who can reach the route can open the demo notices and the complaint. The Production gate above is
/// the control that keeps the route off; do not weaken it, and do not set DevSeed__Enabled on a deployed environment.
/// </remarks>
public static class DevSeedEndpoints
{
    public const string Route = "/dev/seed";

    public const string KeyHeader = "X-Dev-Seed-Key";

    public static WebApplication MapDevSeedEndpoints(this WebApplication app)
    {
        var options = app.Configuration.GetSection(DevSeedOptions.SectionName).Get<DevSeedOptions>() ?? new DevSeedOptions();
        if (!DevSeedOptions.IsEnabled(options, app.Environment))
        {
            return app;
        }

        app.MapPost(Route, SeedAsync);
        return app;
    }

    internal static async Task<IResult> SeedAsync(
        HttpRequest request,
        IOptions<DevSeedOptions> options,
        SubjectHasher hasher,
        DevSeeder seeder,
        CancellationToken ct)
    {
        var presented = request.Headers.TryGetValue(KeyHeader, out var values) ? values.ToString() : string.Empty;
        if (!KeyMatches(options.Value.Key, presented))
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "The dev seed key is missing or wrong.");
        }

        if (!hasher.IsConfigured)
        {
            return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "reviews_unavailable", "Reviews are temporarily unavailable.");
        }

        var (body, error) = await JsonBodyReader.ReadAsync<DevSeedRequest>(request, ct);
        if (body is null)
        {
            return error ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var subjects = body.Subjects;
        var input = new DevSeedSubjects(subjects?.Parent, subjects?.Parent2, subjects?.Owner, subjects?.Moderator);
        var outcome = await seeder.SeedAsync(input, ct);

        return Results.Json(
            new DevSeedResponse(
                outcome.Counts.Reviews,
                outcome.Counts.Notices,
                outcome.Counts.OwnerReplies,
                outcome.Counts.Triage,
                outcome.AlreadySeeded),
            ApiJson.Options);
    }

    /// <summary>Constant-time comparison of the configured key with the presented one. An empty key never matches.</summary>
    internal static bool KeyMatches(string expected, string presented)
    {
        if (expected.Length == 0 || presented.Length == 0)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}
