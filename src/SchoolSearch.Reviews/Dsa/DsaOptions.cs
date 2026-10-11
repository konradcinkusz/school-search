namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// Configuration section "Dsa". The single point of contact (art. 11 and 12 DSA) is configured by the operator.
/// Nothing is invented: when neither value is set, /api/v1/legal/contact answers 503 contact_not_configured.
/// </summary>
public sealed class DsaOptions
{
    public const string SectionName = "Dsa";

    public string? ContactEmail { get; set; }

    public string? ContactUrl { get; set; }
}
