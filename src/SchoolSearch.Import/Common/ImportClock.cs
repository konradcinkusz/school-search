namespace SchoolSearch.Import;

/// <summary>
/// A clock pinned to one instant. Loaders stamp Provenance.AsOf from the clock, so
/// <c>sio --as-of</c> pins the date the file describes instead of the day the import ran.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    /// <summary>A clock at midnight UTC on the given calendar date.</summary>
    public static FixedClock OnDate(DateOnly date) =>
        new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}
