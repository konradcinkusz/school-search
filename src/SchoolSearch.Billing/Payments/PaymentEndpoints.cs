using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Errors;

namespace SchoolSearch.Billing.Payments;

/// <summary>
/// Payment endpoints: the provider webhook (anonymous, HMAC verified over the raw body) and the mock simulator
/// (VerifiedUser, mock mode only). Both answer through <see cref="BillingWebhookHandler.ProcessAsync"/>.
/// </summary>
public static class PaymentEndpoints
{
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>Events the simulator may produce. Each is also a webhook event type.</summary>
    private static readonly string[] SimulatedEvents =
    [
        "payment_succeeded",
        "subscription_cancelled",
        "subscription_ended",
        "renewal_failed",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var billing = app.MapGroup("/api/v1/billing");

        // Anonymous by design: the provider calls it. The HMAC over the raw body is the only authentication.
        billing.MapPost("/webhook", ReceiveWebhook).WithName("BillingWebhook");

        billing.MapPost("/simulate", Simulate)
            .WithName("BillingSimulate")
            .RequireAuthorization(AuthPolicies.VerifiedUser);

        return app;
    }

    internal static async Task<IResult> ReceiveWebhook(
        HttpRequest request,
        PaymentSettings settings,
        BillingWebhookHandler handler,
        CancellationToken ct)
    {
        var (body, error) = await ReadRawBodyAsync(request);
        if (body is null)
        {
            return error!;
        }

        var signature = request.Headers[settings.SignatureHeader].ToString();
        var result = await handler.ProcessAsync(body, signature, ct);
        return Respond(result);
    }

    /// <summary>
    /// Mock-only. Builds a synthetic event for one of the caller's own subscriptions, signs it when a secret exists, and
    /// sends it through the same handler as the webhook. Outside mock mode it answers 403 whatever else is true.
    /// </summary>
    internal static async Task<IResult> Simulate(
        HttpRequest request,
        ClaimsPrincipal user,
        PaymentSettings settings,
        BillingDbContext db,
        BillingWebhookHandler handler,
        SubjectHasher hasher,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (!settings.IsMockMode)
        {
            return ApiErrorWriter.Result(StatusCodes.Status403Forbidden, "forbidden", "Simulation is not available.");
        }

        SimulateRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SimulateRequest>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "invalid_body", "Request body is not valid.");
        }

        if (body is null || body.SubscriptionId == Guid.Empty || body.Event is null || !SimulatedEvents.Contains(body.Event))
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "subscriptionId and a supported event are required.");
        }

        if (!hasher.IsConfigured)
        {
            return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "billing_unavailable", "Billing is not available right now.");
        }

        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        var ownerHash = hasher.Hash(subject);
        var subscriptionId = body.SubscriptionId;
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.OwnerHash == ownerHash, ct);
        if (subscription is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Subscription not found.");
        }

        // Same wire shape as a provider event. No period end is sent, so the defensive single-period grant is exercised.
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = "sim_" + Guid.NewGuid().ToString("N"),
            ["type"] = body.Event,
            ["occurred_at"] = clock.GetUtcNow(),
            ["subscription_id"] = subscription.Id,
            ["school_slug"] = subscription.SchoolSlug,
            ["owner_hash"] = subscription.OwnerHash,
            ["plan_code"] = subscription.PlanCode,
        };

        var raw = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = settings.WebhookSecret is { } secret ? WebhookSignature.Sign(secret, raw) : null;
        var result = await handler.ProcessAsync(raw, signature, ct);

        if (result.Status != WebhookStatus.Accepted)
        {
            return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Simulated event could not be processed.");
        }

        return Results.Ok(new { simulated = true, eventId = result.EventId, outcome = result.Outcome });
    }

    private static IResult Respond(WebhookResult result) => result.Status switch
    {
        WebhookStatus.Accepted or WebhookStatus.Duplicate => Results.Ok(new
        {
            received = true,
            duplicate = result.Status == WebhookStatus.Duplicate,
        }),
        WebhookStatus.InvalidSignature => ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "invalid_signature", result.Message ?? "Webhook signature is missing or invalid."),
        _ => ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "invalid_body", result.Message ?? "Request body is not valid."),
    };

    /// <summary>
    /// Reads the raw body before anything binds it. The signature covers these bytes, so they are read once, verbatim.
    /// Oversize bodies are refused here and by the body-limit middleware.
    /// </summary>
    internal static async Task<(string? Body, IResult? Error)> ReadRawBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (Encoding.UTF8.GetByteCount(text) > MaxBodyBytes)
        {
            return (null, ApiErrorWriter.Result(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "Request body is too large."));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "invalid_body", "Request body is required."));
        }

        return (text, null);
    }

    internal sealed class SimulateRequest
    {
        public Guid SubscriptionId { get; set; }

        public string? Event { get; set; }
    }
}
