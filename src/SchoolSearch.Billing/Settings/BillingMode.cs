namespace SchoolSearch.Billing.Settings;

/// <summary>
/// The resolved billing mode, computed once at startup from Billing:MockMode (default: mock outside Production).
/// Mock mode needs no provider credentials. Outside mock mode the simulate endpoint answers 403 unconditionally.
/// </summary>
public sealed class BillingMode(bool isMock)
{
    public bool IsMock { get; } = isMock;
}
