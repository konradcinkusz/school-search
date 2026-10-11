namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>National baseline for one exam year and subject. Natural key: (ExamYear, Subject).</summary>
public sealed class NationalBaselineRow
{
    public int ExamYear { get; set; }

    public string Subject { get; set; } = string.Empty;

    public double NationalMeanPercent { get; set; }

    /// <summary>Pupil standard deviation in points. Null while the source does not supply it (OPEN item).</summary>
    public double? PupilStdDevPoints { get; set; }

    public Provenance Provenance { get; set; } = new();
}
