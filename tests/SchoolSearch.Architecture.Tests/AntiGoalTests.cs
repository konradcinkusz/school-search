using System.Reflection;
using System.Text.RegularExpressions;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Contracts;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Anti-goal absences (docs/MASTER-PROMPT.md; METRIC-ETHICS section 1: "an anti-goal the architecture
/// cannot express is a rule"). Every production assembly is scanned by reflection: type names,
/// namespaces, member names (fields, properties, methods, events, enum values, compiler-generated
/// backing fields) and method or constructor parameter names.
/// </summary>
/// <remarks>
/// Forbidden vocabulary: person-level concepts the product must never model (teachers, pupils as people,
/// students, nationality, citizenship, foreigners), with the Polish equivalents.
///
/// Allowlist. Before matching, these aggregate-count identifiers are removed from each name. They are the
/// vocabulary of exam statistics and are not person data:
///   PupilsSat, Pupils           counts of pupils who sat an exam or are enrolled (numbers, never rows about a person)
///   PupilStdDevPoints           population standard deviation of exam points (a statistic, not a person)
///   PupilColumn(s), PupilHeader names of source-file columns that hold those counts (import column maps)
///   ForeignLanguage             the exam subject "foreign_language" (Subject.ForeignLanguage)
/// "Pupils" matches only as a whole word, so "PupilsRecord" or "PupilsNames" still fail. The list is short on
/// purpose: adding a term is a reviewed change to AggregateCountVocabulary, not a silent exemption.
/// </remarks>
public sealed class AntiGoalTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Person-level vocabulary. Polish: nauczyciel (teacher), uczen (pupil), obywatel (citizen), narodowosc (nationality).
    private static readonly Regex PersonTerms = new(
        "Teacher|Pupil|Student|Nationality|Citizenship|Foreign|Cudzoziemc|Nauczyciel|Ucze[nń]|Obywatel|Narodowo",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Removed from a name before PersonTerms is applied. See the allowlist in the class remarks.
    private static readonly Regex AggregateCountVocabulary = new(
        "PupilsSat|Pupils(?![A-Za-z])|PupilStdDevPoints|PupilColumns?|PupilHeader|ForeignLanguage",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The governing body's name is never modelled; only its type code is (School.cs remarks, docs/data/SOURCES.md).
    private static readonly Regex OperatorTerms = new(
        "OperatorName|OperatorNazwa|NazwaOperatora|OrganProwadzacy|GoverningBodyName|NazwaOrganu",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // No ascending or bottom ranking exists (METRIC-ETHICS section 1). "Asc" only as a whole token, so "Ascii" passes.
    private static readonly Regex AscendingOrBottomTerms = new(
        "Ascend|Asc(?![a-z])|Bottom|Worst|Lowest",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Every_production_assembly_named_in_the_brief_is_scanned_and_is_not_empty()
    {
        var names = ProductionAssemblies().Select(a => a.GetName().Name).ToArray();

        Assert.Contains("SchoolSearch.Api", names);
        Assert.Contains("SchoolSearch.Catalog.Data", names);
        Assert.Contains("SchoolSearch.Contracts", names);
        Assert.Contains("SchoolSearch.Import", names);

        foreach (var assembly in ProductionAssemblies())
        {
            Assert.NotEmpty(TypesOf(assembly));
        }
    }

    [Fact]
    public void No_type_namespace_or_member_names_a_person_concept_outside_the_aggregate_allowlist()
    {
        var violations = Scan(name => PersonTerms.IsMatch(AggregateCountVocabulary.Replace(name, string.Empty)));

        Assert.Empty(violations);
    }

    [Fact]
    public void No_operator_name_property_field_or_parameter_exists_in_any_production_type_including_contracts()
    {
        var violations = Scan(name => OperatorTerms.IsMatch(name));

        Assert.Empty(violations);
    }

    [Fact]
    public void No_ascending_or_bottom_ranking_name_exists_in_any_production_type()
    {
        var violations = Scan(name => AscendingOrBottomTerms.IsMatch(name));

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_public_contract_type_with_a_numeric_score_also_carries_cohort_size_and_methodology_version()
    {
        // ScoreBlock rule (METRIC-ETHICS section 3): no number leaves without its cohort and methodology.
        var contractTypes = TypesOf(typeof(ScoreDto).Assembly)
            .Where(t => t.IsPublic && (t.Namespace ?? string.Empty).StartsWith("SchoolSearch.Contracts", StringComparison.Ordinal))
            .ToArray();

        // Not vacuous: the existing ScoreDto must be among the checked types.
        Assert.Contains(contractTypes, t => t.Name == nameof(ScoreDto));

        var offenders = new List<string>();
        foreach (var type in contractTypes)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var hasNumericScore = properties.Any(p =>
                string.Equals(p.Name, "Score", StringComparison.OrdinalIgnoreCase) && IsNumeric(p.PropertyType));
            if (!hasNumericScore)
            {
                continue;
            }

            var hasCohortSize = properties.Any(p => string.Equals(p.Name, "CohortSize", StringComparison.OrdinalIgnoreCase));
            var hasMethodologyVersion = properties.Any(p =>
                string.Equals(p.Name, "MethodologyVersion", StringComparison.OrdinalIgnoreCase));

            if (!hasCohortSize || !hasMethodologyVersion)
            {
                offenders.Add(type.FullName ?? type.Name);
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>Returns "Type: name" for every name the predicate flags. Names covered: type, namespace, member, parameter.</summary>
    private static List<string> Scan(Func<string, bool> isViolation)
    {
        var violations = new List<string>();

        foreach (var assembly in ProductionAssemblies())
        {
            foreach (var type in TypesOf(assembly))
            {
                foreach (var name in NamesOf(type))
                {
                    if (isViolation(name))
                    {
                        violations.Add($"{type.FullName}: {name}");
                    }
                }
            }
        }

        return violations;
    }

    private static IEnumerable<string> NamesOf(Type type)
    {
        yield return type.Name;

        if (!string.IsNullOrEmpty(type.Namespace))
        {
            yield return type.Namespace;
        }

        foreach (var member in type.GetMembers(DeclaredMembers))
        {
            yield return member.Name;

            if (member is MethodBase method)
            {
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.Name ?? string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// Real types only. A type that fails to load (a missing dependency) is skipped here rather than
    /// hiding the other types of the assembly; the other tests still see every loadable type.
    /// </summary>
    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static IReadOnlyList<Assembly> ProductionAssemblies() =>
        new[]
        {
            typeof(SchoolSearch.Api.Scoring.ScoringPolicy).Assembly,
            typeof(CatalogSchema).Assembly,
            typeof(ScoreDto).Assembly,
            typeof(SchoolSearch.Import.ImportReport).Assembly,
            typeof(SchoolSearch.ServiceDefaults.CorsPolicies).Assembly,
        };

    private static bool IsNumeric(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(double)
            || underlying == typeof(float)
            || underlying == typeof(decimal)
            || underlying == typeof(int)
            || underlying == typeof(long);
    }
}
