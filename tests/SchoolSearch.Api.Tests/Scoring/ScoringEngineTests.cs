using SchoolSearch.Api.Scoring;
using SchoolSearch.Contracts;

namespace SchoolSearch.Api.Tests.Scoring;

public sealed class ScoringEngineTests
{
    private const int Year = ScoringFixtures.ExamYear;

    [Fact]
    public void Enforcement_a_higher_average_with_lower_coverage_yields_a_lower_published_score()
    {
        var baseline = ScoringFixtures.PolishOnlyBaseline();

        // School X at full coverage: mean 70, 40 of 40 sat, factor 1 -> 70 - 60 = +10.
        var full = ScoringEngine.Score(
            new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 40) }, 40),
            baseline);

        // Same school, higher average (80) but only 20 of 40 sat: coverage 0.5 < floor 0.8,
        // factor 0.5 / 0.8 = 0.625 -> 80 * 0.625 - 60 = -10.
        var partial = ScoringEngine.Score(
            new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 80.0, 20) }, 40),
            baseline);

        Assert.Equal(10.0, full.Score!.Value, 9);
        Assert.Equal(-10.0, partial.Score!.Value, 9);
        Assert.True(partial.Score.Value < full.Score.Value);
        Assert.Equal(40, full.CohortSize);
        Assert.Equal(20, partial.CohortSize);
    }

    [Fact]
    public void Cohort_of_eight_is_suppressed_with_reason_and_no_interval()
    {
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 8) }, 8);

        var result = ScoringEngine.Score(input, ScoringFixtures.PolishOnlyBaseline());

        Assert.Null(result.Score);
        Assert.Equal("insufficient_cohort", result.Reason);
        Assert.Equal(ScoreDto.InsufficientCohortReason, result.Reason);
        Assert.Equal(8, result.CohortSize);
        Assert.Null(result.IntervalLow);
        Assert.Null(result.IntervalHigh);
        Assert.Equal("1.0.0", result.MethodologyVersion);
        Assert.Equal(ScoringPolicy.MethodologyVersion, result.MethodologyVersion);
    }

    [Fact]
    public void Cohort_of_exactly_nine_is_published()
    {
        Assert.Equal(9, ScoringPolicy.PublicationThreshold);
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 9) }, 9);

        var result = ScoringEngine.Score(input, ScoringFixtures.PolishOnlyBaseline());

        Assert.Equal(10.0, result.Score!.Value, 9);
        Assert.Null(result.Reason);
        Assert.Equal(9, result.CohortSize);
        Assert.NotNull(result.IntervalLow);
        Assert.NotNull(result.IntervalHigh);
    }

    [Fact]
    public void Cohort_is_the_largest_pupils_sat_across_subjects()
    {
        // Polish sat 8 and Mathematics sat 9: cohort is 9, so the score is published.
        // Coverage 9 / 9 = 1, factor 1. Deviations: Polish 70 - 65 = +5, Mathematics 62 - 55 = +7; mean 6.
        var input = new SchoolExamInput(Year, new[]
        {
            new SubjectResult(Subject.Polish, 70.0, 8),
            new SubjectResult(Subject.Mathematics, 62.0, 9),
        }, 9);

        var result = ScoringEngine.Score(input, ScoringFixtures.ThreeSubjectBaseline());

        Assert.Equal(6.0, result.Score!.Value, 9);
        Assert.Equal(9, result.CohortSize);
    }

    [Fact]
    public void Headcount_of_zero_is_suppressed_even_with_pupils_who_sat()
    {
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 20) }, 0);

        var result = ScoringEngine.Score(input, ScoringFixtures.PolishOnlyBaseline());

        Assert.Null(result.Score);
        Assert.Equal("insufficient_cohort", result.Reason);
        Assert.Equal(20, result.CohortSize);
        Assert.Null(result.IntervalLow);
        Assert.Null(result.IntervalHigh);
    }

    [Fact]
    public void Published_score_is_the_mean_deviation_from_the_national_baseline_at_full_coverage()
    {
        // Three subjects, headcount 100, cohort = max(90, 95, 92) = 95, coverage 0.95 >= 0.8, factor 1.
        // Deviations: Polish 70 - 65 = +5, Mathematics 62 - 55 = +7, ForeignLanguage 75 - 80 = -5.
        // Score = (5 + 7 - 5) / 3 = 7 / 3.
        var input = new SchoolExamInput(Year, new[]
        {
            new SubjectResult(Subject.Polish, 70.0, 90),
            new SubjectResult(Subject.Mathematics, 62.0, 95),
            new SubjectResult(Subject.ForeignLanguage, 75.0, 92),
        }, 100);

        var result = ScoringEngine.Score(input, ScoringFixtures.ThreeSubjectBaseline());

        Assert.Equal(7.0 / 3.0, result.Score!.Value, 9);
        Assert.Equal(95, result.CohortSize);
        Assert.Null(result.Reason);
        Assert.Equal("1.0.0", result.MethodologyVersion);

        // Interval: se_s = sd_s / sqrt(sat_s) (factor 1); se = sqrt(sum se_s^2) / 3 = 0.6075957;
        // half-width 1.96 * se = 1.1908876; bounds 2.3333333 -/+ that.
        Assert.Equal(1.142446, result.IntervalLow!.Value, 6);
        Assert.Equal(3.524221, result.IntervalHigh!.Value, 6);
    }

    [Fact]
    public void Coverage_exactly_at_the_floor_is_not_discounted()
    {
        // Cohort 80 of headcount 100 gives coverage 0.8 == CoverageFloor, so factor is 1 and score is 70 - 60.
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 80) }, 100);

        var result = ScoringEngine.Score(input, ScoringFixtures.PolishOnlyBaseline());

        Assert.Equal(10.0, result.Score!.Value, 9);
    }

    [Fact]
    public void Published_interval_narrows_when_the_cohort_grows_at_the_same_mean()
    {
        var baseline = ScoringFixtures.PolishOnlyBaseline();

        // sd 20, factor 1. Cohort 100: se = 20 / 10 = 2.0 -> half-width 3.92 -> [6.08, 13.92].
        var small = ScoringEngine.Score(
            new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 100) }, 100),
            baseline);

        // Cohort 400: se = 20 / 20 = 1.0 -> half-width 1.96 -> [8.04, 11.96].
        var large = ScoringEngine.Score(
            new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 400) }, 400),
            baseline);

        Assert.Equal(6.08, small.IntervalLow!.Value, 9);
        Assert.Equal(13.92, small.IntervalHigh!.Value, 9);
        Assert.Equal(8.04, large.IntervalLow!.Value, 9);
        Assert.Equal(11.96, large.IntervalHigh!.Value, 9);

        Assert.Equal(7.84, small.IntervalHigh!.Value - small.IntervalLow!.Value, 9);
        Assert.Equal(3.92, large.IntervalHigh!.Value - large.IntervalLow!.Value, 9);
        Assert.True(large.IntervalHigh.Value - large.IntervalLow.Value < small.IntervalHigh.Value - small.IntervalLow.Value);
    }

    [Fact]
    public void Only_subjects_present_in_both_input_and_baseline_are_used()
    {
        // Baseline has Polish, Mathematics and ForeignLanguage; input has only Polish (70, sat 50, headcount 50).
        // Only Polish counts: 70 - 65 = +5. Mathematics and ForeignLanguage do not dilute the mean.
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 50) }, 50);

        var result = ScoringEngine.Score(input, ScoringFixtures.ThreeSubjectBaseline());

        Assert.Equal(5.0, result.Score!.Value, 9);
    }

    [Fact]
    public void Input_subjects_missing_from_the_baseline_are_ignored()
    {
        // Input has Polish 70 (sat 90) and Mathematics 62 (sat 95); baseline has only Polish and ForeignLanguage.
        // Common subject: Polish only, deviation +5, cohort 95, coverage 0.95 -> score 5.
        var baseline = new NationalBaseline(Year, new[]
        {
            new SubjectBaseline(Subject.Polish, 65.0, 10.0),
            new SubjectBaseline(Subject.ForeignLanguage, 80.0, 8.0),
        });
        var input = new SchoolExamInput(Year, new[]
        {
            new SubjectResult(Subject.Polish, 70.0, 90),
            new SubjectResult(Subject.Mathematics, 62.0, 95),
        }, 100);

        var result = ScoringEngine.Score(input, baseline);

        Assert.Equal(5.0, result.Score!.Value, 9);
        Assert.Equal(95, result.CohortSize);
    }

    [Fact]
    public void No_subject_in_common_throws_invalid_operation()
    {
        var baseline = new NationalBaseline(Year, new[]
        {
            new SubjectBaseline(Subject.Mathematics, 55.0, 12.0),
            new SubjectBaseline(Subject.ForeignLanguage, 80.0, 8.0),
        });
        var input = new SchoolExamInput(Year, new[] { new SubjectResult(Subject.Polish, 70.0, 50) }, 50);

        Assert.Throws<InvalidOperationException>(() => { ScoringEngine.Score(input, baseline); });
    }

    [Fact]
    public void Year_mismatch_throws_argument_exception()
    {
        var input = new SchoolExamInput(2025, new[] { new SubjectResult(Subject.Polish, 70.0, 50) }, 50);
        var baseline = ScoringFixtures.PolishOnlyBaseline(2024);

        Assert.Throws<ArgumentException>(() => { ScoringEngine.Score(input, baseline); });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void Engine_never_produces_a_null_score_without_the_insufficient_cohort_reason(int seed)
    {
        var random = new Random(seed);
        var baseline = ScoringFixtures.ThreeSubjectBaseline();
        var subjects = new List<SubjectResult>();
        foreach (var subject in new[] { Subject.Polish, Subject.Mathematics, Subject.ForeignLanguage })
        {
            if (random.NextDouble() < 0.7)
            {
                subjects.Add(new SubjectResult(subject, random.NextDouble() * 100.0, random.Next(1, 151)));
            }
        }

        // An input with no subject in common throws (data bug), so every generated school lists at least one subject.
        if (subjects.Count == 0)
        {
            subjects.Add(new SubjectResult(Subject.Polish, random.NextDouble() * 100.0, random.Next(1, 151)));
        }

        var headcount = random.Next(0, 201);
        var expectedCohort = subjects.Max(s => s.PupilsSat);
        var expectedSuppressed = headcount <= 0 || expectedCohort < ScoringPolicy.PublicationThreshold;

        var result = ScoringEngine.Score(new SchoolExamInput(Year, subjects, headcount), baseline);

        Assert.Equal(expectedCohort, result.CohortSize);
        Assert.Equal(ScoringPolicy.MethodologyVersion, result.MethodologyVersion);
        Assert.Equal(expectedSuppressed, result.Score is null);

        if (result.Score is null)
        {
            Assert.Equal("insufficient_cohort", result.Reason);
            Assert.Null(result.IntervalLow);
            Assert.Null(result.IntervalHigh);
        }
        else
        {
            Assert.Null(result.Reason);
            Assert.True(result.CohortSize >= ScoringPolicy.PublicationThreshold);
            Assert.NotNull(result.IntervalLow);
            Assert.NotNull(result.IntervalHigh);
            Assert.True(result.IntervalLow!.Value <= result.Score.Value);
            Assert.True(result.Score.Value <= result.IntervalHigh!.Value);
        }
    }
}
