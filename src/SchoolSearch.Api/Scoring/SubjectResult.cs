namespace SchoolSearch.Api.Scoring;

/// <summary>
/// One school's mean exam result in one subject, with the number of pupils who sat it.
/// Validation (0 to 100 mean, non-negative pupil count) happens in <see cref="ScoringEngine"/>.
/// </summary>
/// <param name="Subject">The exam subject.</param>
/// <param name="MeanPercent">The school's mean result in percent (0 to 100).</param>
/// <param name="PupilsSat">The number of pupils who sat this subject.</param>
public sealed record SubjectResult(Subject Subject, double MeanPercent, int PupilsSat);
