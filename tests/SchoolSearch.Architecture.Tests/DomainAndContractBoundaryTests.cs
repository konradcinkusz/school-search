using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Anti-goals from docs/MASTER-PROMPT.md: no teacher or pupil entity (no personal data about
/// people in school), and no nationality or foreigner data ever.
/// </summary>
public sealed class CatalogDataBoundaryTests
{
    private const string ForbiddenNamePattern = "(?i)Teacher|Pupil|Student|Foreigner|Nationality";

    [Fact]
    public void Catalog_Data_contains_types_to_check()
    {
        // The School entity is the minimum the catalog must hold, so the rules below are not vacuous.
        var types = Solution.Load(Solution.CatalogData).GetTypes();
        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.Name == "School");
    }

    [Fact]
    public void No_type_in_Catalog_Data_is_named_Teacher_Pupil_Student_Foreigner_or_Nationality()
    {
        var result = Types.InAssembly(Solution.Load(Solution.CatalogData))
            .ShouldNot()
            .HaveNameMatching(ForbiddenNamePattern)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Forbidden personal-data type name in Catalog.Data: " + Solution.Describe(result.FailingTypes));
    }

    [Fact]
    public void No_namespace_in_Catalog_Data_is_named_for_teachers_pupils_students_foreigners_or_nationality()
    {
        var result = Types.InAssembly(Solution.Load(Solution.CatalogData))
            .ShouldNot()
            .ResideInNamespaceMatching(ForbiddenNamePattern)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Forbidden personal-data namespace in Catalog.Data: " + Solution.Describe(result.FailingTypes));
    }
}

/// <summary>
/// A score is meaningless without the cohort it was computed on and the methodology version
/// that produced it (docs/MASTER-PROMPT.md: every response carries methodologyVersion and cohort).
/// The contract must make a ScoreDto impossible to construct without both.
/// </summary>
public sealed class ScoreContractTests
{
    [Fact]
    public void ScoreDto_cannot_be_built_without_cohort_size_and_methodology_version()
    {
        var scoreDto = Solution.Load(Solution.Contracts).GetTypes().SingleOrDefault(t => t.Name == "ScoreDto");
        Assert.NotNull(scoreDto);

        var constructors = scoreDto!.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(constructors);

        var everyConstructorRequiresBoth = constructors.All(c =>
        {
            var names = c.GetParameters().Select(p => p.Name ?? string.Empty).ToArray();
            return names.Any(n => n.Contains("cohort", StringComparison.OrdinalIgnoreCase))
                && names.Any(n => n.Contains("methodologyversion", StringComparison.OrdinalIgnoreCase));
        });

        // Alternative accepted by C#: a parameterless constructor is allowed only when the
        // cohort and methodology members are C# `required` members, which the compiler enforces.
        var requiredMemberFlags = scoreDto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("cohort", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("methodologyversion", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.GetCustomAttributes().Any(a =>
                a.GetType().FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute"))
            .ToArray();
        var requiredMembersEnforced = requiredMemberFlags.Length >= 2 && requiredMemberFlags.All(f => f);

        Assert.True(everyConstructorRequiresBoth || requiredMembersEnforced,
            "ScoreDto can be constructed without cohort size or methodologyVersion.");
    }
}
