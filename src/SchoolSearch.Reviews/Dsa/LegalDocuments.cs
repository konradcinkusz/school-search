namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// Legal texts that the DSA endpoints publish. Every text is a DRAFT marked for review by a lawyer; none is legal advice.
/// The terms version is not here: it is ReviewsOptions.TermsVersion, the same value reviews must accept.
/// </summary>
public static class LegalDocuments
{
    public const string TermsStatus = "projekt do weryfikacji przez prawnika";

    public const string DraftNotice = "Dokument roboczy: projekt do weryfikacji przez prawnika. Nie stanowi porady prawnej.";

    /// <summary>Redress information required in every statement of reasons (art. 17 DSA).</summary>
    public const string RedressInfo =
        "Od tej decyzji można złożyć skargę wewnętrzną (art. 20 DSA) przez POST /api/v1/decisions/{id}/complaints, z uzasadnieniem. " +
        "Można też skorzystać z pozasądowego rozstrzygania sporów (art. 21 DSA) lub z drogi sądowej.";

    public const string ContactRole = "Punkt kontaktowy (art. 11 i 12 DSA)";
}
