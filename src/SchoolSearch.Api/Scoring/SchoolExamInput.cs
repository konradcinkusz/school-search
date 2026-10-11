namespace SchoolSearch.Api.Scoring;

/// <summary>
/// Everything the engine needs for one school in one exam year.
/// </summary>
/// <param name="Year">The exam year. Must equal the baseline year.</param>
/// <param name="Subjects">The school's per-subject results.</param>
/// <param name="ClassVIIIHeadcount">The class VIII headcount (the coverage denominator, from SIO).</param>
public sealed record SchoolExamInput(int Year, IReadOnlyList<SubjectResult> Subjects, int ClassVIIIHeadcount);
