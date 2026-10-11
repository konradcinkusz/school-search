using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Anti-goal and phase F gate (docs/MASTER-PROMPT.md): scoring must never reference billing,
/// entitlement, payment or user code. Scoring is the engine that will rank schools; a
/// paywall or account concern inside it would make the ranking depend on who pays.
/// </summary>
public sealed class ScoringBoundaryTests
{
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";

    private static readonly string[] ForbiddenWords = { "Billing", "Entitlement", "Payment", "User" };

    // Namespaces the solution is known to use for these concerns, checked even when they do not exist yet.
    private static readonly string[] ForbiddenNamespacePrefixes =
    {
        "SchoolSearch.Billing",
        "SchoolSearch.Entitlements",
        "SchoolSearch.Payments",
        "SchoolSearch.Users",
    };

    [Fact]
    public void Scoring_namespace_has_no_dependency_on_forbidden_namespaces()
    {
        // Namespaces that exist anywhere in the production solution and contain a forbidden word
        // are forbidden targets too, whatever their exact name turns out to be.
        var discovered = Solution.AllProductionAssemblies()
            .SelectMany(a => a.GetTypes())
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ForbiddenWords.Any(w => ns.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToArray();

        var forbidden = ForbiddenNamespacePrefixes.Concat(discovered).Distinct().ToArray();

        var result = Types.InAssembly(Solution.Load(Solution.Api))
            .That()
            .ResideInNamespace(ScoringNamespace)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Api.Scoring depends on a forbidden namespace. Offending types: "
            + Solution.Describe(result.FailingTypes));
    }

    [Fact]
    public void Scoring_type_signatures_reference_no_type_whose_name_or_assembly_mentions_a_forbidden_concern()
    {
        // Reflection complement to the IL-level rule above: checks every type that a scoring type
        // exposes in its shape (base, interfaces, members, signatures) by name and by assembly.
        var scoringTypes = Solution.Load(Solution.Api).GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(ScoringNamespace, StringComparison.Ordinal))
            .ToArray();

        var offenders = new List<string>();
        foreach (var type in scoringTypes)
        {
            foreach (var referenced in ShapeReferencedTypes(type))
            {
                var assemblyName = referenced.Assembly.GetName().Name ?? string.Empty;
                var fullName = referenced.FullName ?? referenced.Name;
                if (ForbiddenWords.Any(w =>
                        fullName.Contains(w, StringComparison.OrdinalIgnoreCase)
                        || assemblyName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{type.FullName} -> {fullName}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scoring_namespace_rule_is_checked_against_a_real_assembly()
    {
        // The Api assembly itself must load and be scanned; an unloaded assembly would make
        // the rules above pass for the wrong reason.
        Assert.NotEmpty(Solution.Load(Solution.Api).GetTypes());
    }

    private static IEnumerable<Type> ShapeReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }

        foreach (var field in type.GetFields(all))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(all))
        {
            yield return property.PropertyType;
        }

        foreach (var ctor in type.GetConstructors(all))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(all))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
