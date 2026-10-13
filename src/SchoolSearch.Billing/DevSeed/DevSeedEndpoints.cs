using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Security;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>
/// <c>POST /dev/seed</c>: the local and dev seed of the billing data (ADR-0016 mock mode). Anonymous by design: the gate
/// is configuration plus the key header, and the route answers 404 unless <see cref="DevSeedGate"/> allows it.
/// </summary>
/// <remarks>
/// Never enable this outside local development. The seeded rows use fixed ids and a mock provider reference, so they are
/// predictable. The Production check in <see cref="DevSeedGate.Decide"/> is the control that keeps the route off; the key
/// header is a second factor, not the reason it is safe. This endpoint does not read Billing:MockMode, so mock mode alone
/// does not protect it. Do not set DevSeed__Enabled on a deployed environment.
/// </remarks>
public static class DevSeedEndpoints
{
    public const int MaxSubjectLength = 200;

    public static IEndpointRouteBuilder MapDevSeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/seed", PostSeed).WithName("DevSeedBilling");
        return app;
    }

    internal static async Task<IResult> PostSeed(
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment,
        SubjectHasher hasher,
        DevSeeder seeder,
        CancellationToken ct)
    {
        var decision = DevSeedGate.Decide(
            DevSeedGate.IsEnabled(configuration[DevSeedGate.EnabledKey]),
            configuration[DevSeedGate.KeyKey],
            environment.IsProduction(),
            request.Headers[DevSeedGate.HeaderName].ToString());

        if (decision == DevSeedDecision.NotFound)
        {
            return Results.NotFound();
        }

        if (decision == DevSeedDecision.Unauthorized)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "The dev seed key is missing or wrong.");
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<DevSeedRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var owner = Clean(body.Subjects?.Owner);
        var moderator = Clean(body.Subjects?.Moderator);
        if (owner is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "subjects.owner is required.");
        }

        if (owner.Length > MaxSubjectLength || (moderator is not null && moderator.Length > MaxSubjectLength))
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "A subject is too long.");
        }

        if (!hasher.IsConfigured)
        {
            return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "billing_unavailable", "Billing is not available right now.");
        }

        var response = await seeder.SeedAsync(owner, moderator, ct);
        return Results.Json(response, ApiJson.Options);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
