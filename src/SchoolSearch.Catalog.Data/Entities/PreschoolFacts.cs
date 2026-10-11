namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// Practical facts about one preschool: monthly tuition (czesne, PLN), optional meal fee, opening
/// hours and free places. Every fact is published with its own provenance. Unknown values stay null;
/// nothing is estimated. Shares its key with <see cref="Preschool"/> (one-to-one, cascade delete).
/// </summary>
/// <remarks>
/// Check constraints: amounts and free places are non-negative, the minimum tuition is not above the
/// maximum, and Confidence is "reported" or "unverified" (the database enforces these; see the Phase D migration).
/// </remarks>
public sealed class PreschoolFacts
{
    /// <summary>Primary key and foreign key to <see cref="Preschool.IdPodmiot"/>.</summary>
    public long PreschoolId { get; set; }

    /// <summary>Monthly tuition (czesne), lower bound in PLN, numeric(10,2), at least 0.</summary>
    public decimal? MonthlyTuitionMinPln { get; set; }

    /// <summary>Monthly tuition (czesne), upper bound in PLN, numeric(10,2), at least the minimum.</summary>
    public decimal? MonthlyTuitionMaxPln { get; set; }

    /// <summary>Monthly meal fee (wyżywienie) in PLN, numeric(10,2), at least 0, when published.</summary>
    public decimal? MonthlyMealPln { get; set; }

    /// <summary>Opening time, when published.</summary>
    public TimeOnly? OpensAt { get; set; }

    /// <summary>Closing time, when published.</summary>
    public TimeOnly? ClosesAt { get; set; }

    /// <summary>Number of free places (vacancies), at least 0, when published.</summary>
    public int? FreePlaces { get; set; }

    /// <summary>Date the free places count refers to. Required in practice whenever FreePlaces is set.</summary>
    public DateOnly? FreePlacesAsOf { get; set; }

    /// <summary>"reported" (from a primary public source, loaded from file) or "unverified" (submitted, not checked).</summary>
    public string Confidence { get; set; } = "reported";

    public Provenance Provenance { get; set; } = new();
}
