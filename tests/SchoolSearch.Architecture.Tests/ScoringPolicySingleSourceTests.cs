using System.Reflection;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// ADR-0009: the publication threshold lives in exactly one place, ScoringPolicy.PublicationThreshold.
/// Checked by reflection over the Api.Scoring namespace so a second copy of the number 9, or a
/// second threshold constant, fails the build.
/// </summary>
public sealed class ScoringPolicySingleSourceTests
{
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";
    private const string PolicyTypeName = "SchoolSearch.Api.Scoring.ScoringPolicy";
    private const string ThresholdFieldName = "PublicationThreshold";
    private const int ThresholdValue = 9;

    private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void ScoringPolicy_publication_threshold_is_nine()
    {
        var policy = Solution.Load(Solution.Api).GetType(PolicyTypeName, throwOnError: true)!;
        var field = policy.GetField(ThresholdFieldName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);
        Assert.Equal(ThresholdValue, Convert.ToInt32(field.GetRawConstantValue()));
    }

    [Fact]
    public void No_other_threshold_constant_exists_in_the_scoring_namespace()
    {
        var intConstants = ScoringTypes()
            .SelectMany(t => t.GetFields(AllDeclared)
                .Where(f => f.IsLiteral && f.FieldType == typeof(int))
                .Select(f => (Owner: t.FullName, f.Name)))
            .ToArray();

        var thresholdNamed = intConstants
            .Where(c => c.Name.Contains("Threshold", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{c.Owner}.{c.Name}")
            .ToArray();

        Assert.Equal(new[] { $"{PolicyTypeName}.{ThresholdFieldName}" }, thresholdNamed);
    }

    [Fact]
    public void The_number_nine_appears_as_an_int_constant_only_as_the_publication_threshold()
    {
        var intConstantsWithValueNine = ScoringTypes()
            .SelectMany(t => t.GetFields(AllDeclared)
                .Where(f => f.IsLiteral && f.FieldType == typeof(int))
                .Where(f => Convert.ToInt32(f.GetRawConstantValue()) == ThresholdValue)
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToArray();

        Assert.Equal(new[] { $"{PolicyTypeName}.{ThresholdFieldName}" }, intConstantsWithValueNine);
    }

    private static Type[] ScoringTypes() =>
        Solution.Load(Solution.Api).GetTypes()
            .Where(t => t.Namespace == ScoringNamespace)
            .ToArray();
}
