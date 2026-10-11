namespace SchoolSearch.Api.Scoring;

/// <summary>
/// The national reference for one exam year: one <see cref="SubjectBaseline"/> per subject.
/// Duplicate subjects are rejected by <see cref="ScoringEngine"/>.
/// </summary>
/// <param name="Year">The exam year this baseline describes.</param>
/// <param name="Subjects">The per-subject national references.</param>
public sealed record NationalBaseline(int Year, IReadOnlyList<SubjectBaseline> Subjects);
