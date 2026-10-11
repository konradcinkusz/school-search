using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// ADR-0014 (docs/MASTER-PROMPT.md, Phase D): preschools never get a quality score, rank, grade or any
/// ordinal judgement. Preschool types show practical facts only. The absence is enforced by reflection
/// over the entity and public contract types named Preschool*, and by a dependency rule: no Preschool*
/// type may reference the scoring engine.
/// </summary>
public sealed class PreschoolNoScoreTests
{
    private const string EntityNamespace = "SchoolSearch.Catalog.Data.Entities";
    private const string PublicContractNamespace = "SchoolSearch.Contracts.Public";
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";

    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Judgement vocabulary. "rank" also matches "ranking"; "quality" also matches "equality", which is why
    // the compiler-generated record member EqualityContract is excluded below.
    private static readonly Regex JudgementTerms = new(
        "score|rank|rating|grade|quality|best|worst",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Preschool_entities_exist_so_the_absence_rules_are_not_vacuous()
    {
        var names = PreschoolTypes(Solution.CatalogData, EntityNamespace).Select(t => t.Name).ToArray();

        Assert.Contains("Preschool", names);
        Assert.Contains("PreschoolFacts", names);
    }

    [Fact]
    public void No_Preschool_type_has_a_name_or_member_that_is_a_score_rank_rating_grade_quality_best_or_worst()
    {
        var offenders = new List<string>();

        foreach (var type in PreschoolTypes(Solution.CatalogData, EntityNamespace)
                     .Concat(PreschoolTypes(Solution.Contracts, PublicContractNamespace)))
        {
            if (JudgementTerms.IsMatch(type.Name))
            {
                offenders.Add($"{type.FullName} (type name)");
            }

            foreach (var property in type.GetProperties(DeclaredMembers))
            {
                if (property.Name != "EqualityContract" && JudgementTerms.IsMatch(property.Name))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }

            foreach (var field in type.GetFields(DeclaredMembers))
            {
                if (!field.Name.StartsWith('<') && JudgementTerms.IsMatch(field.Name))
                {
                    offenders.Add($"{type.FullName}.{field.Name} (field)");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Preschool_entities_do_not_reference_the_scoring_namespace()
    {
        var result = Types.InAssembly(Solution.Load(Solution.CatalogData))
            .That()
            .HaveNameStartingWith("Preschool")
            .ShouldNot()
            .HaveDependencyOn(ScoringNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Preschool entities must not reference Api.Scoring. Offending types: " + Solution.Describe(result.FailingTypes));
    }

    [Fact]
    public void Preschool_public_contracts_do_not_reference_the_scoring_namespace()
    {
        var result = Types.InAssembly(Solution.Load(Solution.Contracts))
            .That()
            .ResideInNamespace(PublicContractNamespace)
            .And()
            .HaveNameStartingWith("Preschool")
            .ShouldNot()
            .HaveDependencyOn(ScoringNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Preschool public contracts must not reference Api.Scoring. Offending types: " + Solution.Describe(result.FailingTypes));
    }

    private static IEnumerable<Type> PreschoolTypes(string assemblyName, string ns) =>
        Solution.Load(assemblyName).GetTypes()
            .Where(t => t.Namespace == ns && t.Name.StartsWith("Preschool", StringComparison.Ordinal));
}
