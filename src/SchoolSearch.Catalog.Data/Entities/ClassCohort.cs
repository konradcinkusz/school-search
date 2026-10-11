namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>Pupil and class-count figures for one school, year and class level (SIO). Natural key: (SchoolId, SchoolYear, ClassLevel).</summary>
public sealed class ClassCohort
{
    public long SchoolId { get; set; }

    public int SchoolYear { get; set; }

    public string ClassLevel { get; set; } = string.Empty;

    public int Pupils { get; set; }

    /// <summary>Number of divisions (oddziały).</summary>
    public int Oddzialy { get; set; }

    public Provenance Provenance { get; set; } = new();
}
