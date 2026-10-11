using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// P2: the shared kernel holds plumbing only and must never reference a business or service
/// type. ServiceDefaults sits below Catalog.Data and Api in the dependency graph.
/// </summary>
public sealed class KernelBoundaryTests
{
    [Fact]
    public void ServiceDefaults_contains_types_to_check()
    {
        // Guards against the rule below passing vacuously because the assembly was empty or unloaded.
        var types = Solution.Load(Solution.ServiceDefaults).GetTypes();
        Assert.NotEmpty(types);
    }

    [Fact]
    public void ServiceDefaults_references_no_type_from_Catalog_Data_or_Api()
    {
        var result = Types.InAssembly(Solution.Load(Solution.ServiceDefaults))
            .ShouldNot()
            .HaveDependencyOnAny(Solution.CatalogData, Solution.Api)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "ServiceDefaults must not depend on Catalog.Data or Api. Offending types: "
            + Solution.Describe(result.FailingTypes));
    }

    [Fact]
    public void Rule_engine_detects_a_known_dependency_so_the_boundary_rules_are_not_vacuous()
    {
        // Catalog.Data depends on Entity Framework by design. If the engine reports that
        // dependency as absent, the negative rules above would prove nothing.
        var result = Types.InAssembly(Solution.Load(Solution.CatalogData))
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.False(result.IsSuccessful, "Expected the engine to detect the EF Core dependency of Catalog.Data.");
    }
}
