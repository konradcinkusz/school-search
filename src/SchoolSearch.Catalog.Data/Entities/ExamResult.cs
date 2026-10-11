namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A school's mean exam result for one subject and year (CKE). Subject is one of
/// "polish", "mathematics", "foreign_language". Natural key: (SchoolId, ExamYear, Subject).
/// </summary>
public sealed class ExamResult
{
    public long SchoolId { get; set; }

    public int ExamYear { get; set; }

    public string Subject { get; set; } = string.Empty;

    public double MeanPercent { get; set; }

    public int PupilsSat { get; set; }

    public Provenance Provenance { get; set; } = new();
}
