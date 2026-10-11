using SchoolSearch.Api.Scoring;

namespace SchoolSearch.Api.Tests.Scoring;

/// <summary>
/// Synthetic national baselines for the scoring tests. Numbers are invented for arithmetic
/// checks only; they are not data from any exam publication.
/// </summary>
internal static class ScoringFixtures
{
    public const int ExamYear = 2025;

    /// <summary>Polish only: national mean 60, pupil standard deviation 20 points.</summary>
    public static NationalBaseline PolishOnlyBaseline(int year = ExamYear) =>
        new(year, new[] { new SubjectBaseline(Subject.Polish, 60.0, 20.0) });

    /// <summary>Polish 65 (sd 10), Mathematics 55 (sd 12), ForeignLanguage 80 (sd 8).</summary>
    public static NationalBaseline ThreeSubjectBaseline(int year = ExamYear) =>
        new(year, new[]
        {
            new SubjectBaseline(Subject.Polish, 65.0, 10.0),
            new SubjectBaseline(Subject.Mathematics, 55.0, 12.0),
            new SubjectBaseline(Subject.ForeignLanguage, 80.0, 8.0),
        });
}
