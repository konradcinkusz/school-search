namespace SchoolSearch.Reviews.Data;

/// <summary>
/// AI advisory label for a pending review (E6). It only orders the moderator queue. It never publishes,
/// rejects, removes or marks a decision as automated, and the note is never shown to users.
/// </summary>
public sealed class ModerationTriage
{
    /// <summary>Primary key and foreign key to the triaged review.</summary>
    public Guid ReviewId { get; set; }

    /// <summary>One of <see cref="TriageLabels"/>.</summary>
    public string Label { get; set; } = "";

    /// <summary>Up to 300 characters, never user-facing.</summary>
    public string Note { get; set; } = "";

    public string Model { get; set; } = "";

    public DateTimeOffset At { get; set; }
}
