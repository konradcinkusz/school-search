namespace SchoolSearch.Billing.Payments;

/// <summary>Paid-through date arithmetic. Pure functions, so the lifecycle rules can be tested without a host.</summary>
public static class PeriodMath
{
    /// <summary>
    /// New paid-through date after a payment. The base is max(current end, payment date), so renewing early never
    /// shortens days already paid. The provider's period end is used when it is later than the base. Otherwise, one
    /// period is added to the base (the defensive single-period grant).
    /// </summary>
    public static DateTimeOffset NewPeriodEnd(
        DateTimeOffset? currentEnd,
        DateTimeOffset paidAt,
        DateTimeOffset? providerEnd,
        string? interval)
    {
        var basis = currentEnd is { } end && end > paidAt ? end : paidAt;

        if (providerEnd is { } fresh && fresh > basis)
        {
            return fresh;
        }

        return AddPeriod(basis, interval ?? throw new InvalidOperationException("Plan interval is not configured."));
    }

    /// <summary>Adds one billing period. Only "month" and "year" exist.</summary>
    public static DateTimeOffset AddPeriod(DateTimeOffset from, string interval) => interval.ToLowerInvariant() switch
    {
        "month" => from.AddMonths(1),
        "year" => from.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be month or year."),
    };
}
