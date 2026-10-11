using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Monetisation boundary (ADR-0016, Phase F gate in docs/MASTER-PROMPT.md). A paid school profile must never change a
/// score, a rank, the order of a list or the visibility of a review. These rules make that a code absence:
/// scoring, the catalog, reviews and the public score contract have no path to billing or entitlement, and billing has
/// no path back into the scoring code or the catalog database.
/// </summary>
/// <remarks>
/// SchoolSearch.Billing is referenced only once its project file exists (see the csproj), like SchoolSearch.Reviews
/// before it. Rules that name Billing are string checks until the assembly is loaded. Once the project exists,
/// <see cref="Billing_assembly_is_loaded_once_its_project_exists"/> makes sure they are not vacuous.
/// The sort rule reads the Api source as a proxy for its OpenAPI document, because no document is generated in tests.
/// </remarks>
public sealed class MonetisationBoundaryTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const string BillingAssemblyName = "SchoolSearch.Billing";
    private const string BillingNamespace = "SchoolSearch.Billing";
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";
    private const string CatalogDataNamespace = "SchoolSearch.Catalog.Data";

    // Assemblies that carry scores, rankings or reviews. None of them may reference billing (ADR-0016).
    private static readonly string[] ScoreConsumerAssemblyNames =
    {
        "SchoolSearch.Api",
        "SchoolSearch.Reviews",
        "SchoolSearch.Catalog.Data",
        "SchoolSearch.Contracts",
    };

    // The public score and ranking contract. The five names are checked for presence, so the member rule cannot pass
    // vacuously.
    private static readonly string[] PublicScoreContractTypeNames =
    {
        "ScoreBlock",
        "ScoreDto",
        "SchoolListItem",
        "RankingPage",
        "RankedItem",
    };

    // Paid, sponsored or entitlement vocabulary may not appear in a score or ranking member.
    private static readonly Regex PaidVocabulary = new(
        "premium|sponsor|paid|billing|entitle|subscription",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A paid or premium order word, checked only in files that declare a sort option.
    private static readonly Regex PaidOrderTerms = new(
        "premium|paid",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Billing DTOs must not expose a score, a rank or a rating.
    private static readonly Regex ScoreLikeVocabulary = new(
        "score|rank|rating",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Billing_assembly_is_loaded_once_its_project_exists()
    {
        // Not landed yet: the billing rules pass vacuously, as ADR-0015 records for Reviews. Nothing else to check.
        if (!File.Exists(BillingProjectPath()))
        {
            return;
        }

        var billing = TryLoad(BillingAssemblyName);
        Assert.NotNull(billing);
        Assert.NotEmpty(billing!.GetTypes());
    }

    [Fact]
    public void Score_consumers_have_no_dependency_on_billing()
    {
        var offenders = new List<string>();
        foreach (var name in ScoreConsumerAssemblyNames)
        {
            var assembly = TryLoad(name);
            if (assembly is null)
            {
                continue;
            }

            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(BillingNamespace)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.Add($"{name}: {Describe(result.FailingTypes)}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scoring_namespace_has_no_dependency_on_billing_or_entitlement()
    {
        var api = Assembly.Load("SchoolSearch.Api");

        // Non-vacuity: the scoring namespace must exist in the Api assembly, or this rule proves nothing.
        Assert.NotEmpty(api.GetTypes().Where(t => t.Namespace == ScoringNamespace));

        var result = Types.InAssembly(api)
            .That()
            .ResideInNamespace(ScoringNamespace)
            .ShouldNot()
            .HaveDependencyOn(BillingNamespace)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Api.Scoring must never reference billing: a paid profile must not change a score or a rank. Offending types: "
            + Describe(result.FailingTypes));
    }

    [Fact]
    public void Billing_has_no_dependency_on_scoring_or_the_catalog_database()
    {
        var billing = TryLoad(BillingAssemblyName);
        if (billing is null)
        {
            return;
        }

        var scoringResult = Types.InAssembly(billing)
            .ShouldNot()
            .HaveDependencyOn(ScoringNamespace)
            .GetResult();

        var catalogResult = Types.InAssembly(billing)
            .ShouldNot()
            .HaveDependencyOn(CatalogDataNamespace)
            .GetResult();

        Assert.True(
            scoringResult.IsSuccessful,
            "Billing must not reference the scoring engine. Offending types: " + Describe(scoringResult.FailingTypes));
        Assert.True(
            catalogResult.IsSuccessful,
            "Billing owns billingdb only and must not reference the catalog data. Offending types: " + Describe(catalogResult.FailingTypes));
    }

    [Fact]
    public void Public_score_contract_members_carry_no_paid_sponsor_or_entitlement_vocabulary()
    {
        var contracts = Assembly.Load("SchoolSearch.Contracts");
        var scoreTypes = contracts.GetTypes()
            .Where(t => PublicScoreContractTypeNames.Contains(t.Name, StringComparer.Ordinal))
            .ToArray();

        // Non-vacuity: every named contract type must exist.
        Assert.Equal(PublicScoreContractTypeNames.Length, scoreTypes.Length);

        var offenders = new List<string>();
        foreach (var type in scoreTypes)
        {
            foreach (var member in type.GetMembers(DeclaredMembers))
            {
                if (PaidVocabulary.IsMatch(member.Name))
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }

            foreach (var ctor in type.GetConstructors(DeclaredMembers))
            {
                foreach (var parameter in ctor.GetParameters())
                {
                    if (parameter.Name is not null && PaidVocabulary.IsMatch(parameter.Name))
                    {
                        offenders.Add($"{type.FullName}(ctor parameter {parameter.Name})");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Billing_public_types_expose_no_score_rank_or_rating_member()
    {
        var billing = TryLoad(BillingAssemblyName);
        if (billing is null)
        {
            return;
        }

        var offenders = new List<string>();
        foreach (var type in billing.GetTypes().Where(t => t.IsPublic && t.Namespace is not null && t.Namespace.StartsWith(BillingNamespace, StringComparison.Ordinal)))
        {
            foreach (var member in type.GetMembers(DeclaredMembers | BindingFlags.Public))
            {
                if (ScoreLikeVocabulary.IsMatch(member.Name))
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Api_sort_options_never_name_a_paid_or_premium_order()
    {
        // Sort is by name or by score only (docs/MASTER-PROMPT.md). A paid order would be a ranking that money decides.
        var root = FindRepositoryRoot();
        var apiFiles = Directory.GetFiles(Path.Combine(root, "src", "SchoolSearch.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var sortFiles = apiFiles.Where(f => File.ReadAllText(f).Contains("sort", StringComparison.OrdinalIgnoreCase)).ToArray();

        // Non-vacuity: the search endpoint declares its sort parameter.
        Assert.NotEmpty(sortFiles);

        var offenders = new List<string>();
        foreach (var file in sortFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (PaidOrderTerms.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string BillingProjectPath() =>
        Path.Combine(FindRepositoryRoot(), "src", "SchoolSearch.Billing", "SchoolSearch.Billing.csproj");

    /// <summary>Loads an assembly by simple name, or returns null when this test run does not reference it.</summary>
    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static string Describe(IEnumerable<Type>? types) =>
        string.Join(", ", (types ?? Enumerable.Empty<Type>()).Select(t => t.FullName ?? t.Name));
}
