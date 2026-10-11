using System.Linq;
using System.Reflection;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Assemblies are loaded by simple name so these tests do not depend on the namespaces or
/// type names of production code beyond what each rule explicitly names.
/// </summary>
internal static class Solution
{
    public const string ServiceDefaults = "SchoolSearch.ServiceDefaults";
    public const string CatalogData = "SchoolSearch.Catalog.Data";
    public const string Api = "SchoolSearch.Api";
    public const string Contracts = "SchoolSearch.Contracts";

    public static Assembly Load(string simpleName) => Assembly.Load(simpleName);

    public static IEnumerable<Assembly> AllProductionAssemblies() =>
        new[] { ServiceDefaults, CatalogData, Api, Contracts }.Select(Load);

    // NetArchTest returns a null FailingTypes when nothing failed.
    public static string Describe(IEnumerable<Type>? types) =>
        string.Join(", ", (types ?? Enumerable.Empty<Type>()).Select(t => t.FullName ?? t.Name));
}
