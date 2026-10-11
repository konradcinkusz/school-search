namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Versioned consent text for parent enquiries. DRAFT: the Polish wording is pending lawyer review and is marked as
/// such in the response (status "draft"). Every stored enquiry records the version it was accepted under.
/// </summary>
public static class LeadConsent
{
    /// <summary>Bump when the text changes; clients must echo the current version back with each enquiry.</summary>
    public const string Version = "enquiry-consent-draft-1";

    public const string Status = "draft";

    public const string Text =
        "Wyrażam zgodę na przekazanie mojego adresu e-mail oraz treści wiadomości właścicielowi szkoły " +
        "w celu udzielenia mi odpowiedzi. Wiadomość jest widoczna wyłącznie w panelu właściciela szkoły. " +
        "Dane nie będą wykorzystywane do celów marketingowych ani udostępniane publicznie. " +
        "Wiadomość zostanie usunięta po 90 dniach.";
}
