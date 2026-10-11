namespace SchoolSearch.Billing.Data;

/// <summary>
/// A processed provider event. The event id is the primary key, so a redelivery is a no-op (idempotency on the event id).
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>Provider event id, up to 200 characters.</summary>
    public string EventId { get; set; } = "";

    /// <summary>Normalised event type name, up to 100 characters.</summary>
    public string Type { get; set; } = "";

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>One of <see cref="WebhookOutcomes"/>.</summary>
    public string Outcome { get; set; } = WebhookOutcomes.Applied;
}
