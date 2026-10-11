using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// AI boundary (ADR-0011, ADR-0017, docs/ai/AI-FEATURES.md section 1). The Anthropic SDK is reached from one small file
/// per deployable, and the code that scores, stores catalog data or runs the import's extractors has no path to any AI
/// type or to the database. These rules make that a code absence, not a convention.
/// </summary>
/// <remarks>
/// Source scans read the files under src/ as text, so they hold for projects that are not referenced by this test
/// project. Namespace rules use the loaded assemblies. A rule that names an assembly or namespace that does not exist
/// yet is vacuous, as the Reviews and Billing rules were before those projects landed; the non-vacuity asserts below
/// make the rules fail loudly once their subject exists and then disappears.
/// </remarks>
public sealed class AiBoundaryTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const string AnthropicNamespace = "Anthropic";
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";
    private const string CatalogDataNamespace = "SchoolSearch.Catalog.Data";
    private const string EntityFrameworkNamespace = "Microsoft.EntityFrameworkCore";
    private const string ApiAiNamespace = "SchoolSearch.Api.Ai";
    private const string McpAgentNamespace = "SchoolSearch.Mcp.Agent";

    // A file may reference the SDK only if its name says it is the SDK boundary: Anthropic*Client.cs or *Llm.cs.
    private static readonly Regex AllowedSdkFileName = new(
        @"^(Anthropic[A-Za-z0-9_]*Client|[A-Za-z0-9_]*Llm)\.cs$",
        RegexOptions.CultureInvariant);

    // "using Anthropic;", "using Anthropic.Models.Messages;", "using static Anthropic.X;" at the start of a line.
    private static readonly Regex AnthropicUsing = new(
        @"^\s*using\s+(static\s+)?Anthropic(\.[A-Za-z0-9_.]+)?\s*;",
        RegexOptions.CultureInvariant);

    // A qualified use of the SDK namespace in code: "Anthropic.Models", "global::Anthropic.AnthropicClient".
    private static readonly Regex AnthropicQualified = new(
        @"(?<![A-Za-z0-9_])Anthropic\.[A-Z][A-Za-z0-9_]*",
        RegexOptions.CultureInvariant);

    // AI namespaces are the ".Ai" folders of the deployables: SchoolSearch.Api.Ai, SchoolSearch.Reviews.Ai, ...
    private static readonly Regex AiNamespace = new(
        @"\.Ai(\.|$)",
        RegexOptions.CultureInvariant);

    // Extraction code that reads a saved document and writes a candidate file. Import only.
    private static readonly Regex ImportExtractNamespace = new(
        @"\.(Ai|Extract)(\.|$)",
        RegexOptions.CultureInvariant);

    // Methods named like a state change may not take a DbContext: a model answer must never reach a write path.
    private static readonly Regex MutatorMethodName = new(
        "^(Publish|Approve|Reject|Save|Persist|Insert|Upsert|Delete|Remove|Write|Decide|Moderate|Apply)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Ai_namespace_is_present_so_the_AI_rules_are_not_vacuous()
    {
        var api = Assembly.Load("SchoolSearch.Api");

        Assert.Contains(api.GetTypes(), t => t.Namespace == ApiAiNamespace);
    }

    [Fact]
    public void Anthropic_SDK_is_referenced_only_by_files_named_as_the_SDK_boundary()
    {
        var root = FindRepositoryRoot();
        var referencing = SourceFiles(root)
            .Where(f => ReferencesAnthropic(File.ReadAllLines(f)))
            .ToArray();

        // Non-vacuity: the Api's explain client is the SDK boundary today.
        Assert.NotEmpty(referencing);

        var offenders = referencing
            .Where(f => !AllowedSdkFileName.IsMatch(Path.GetFileName(f)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Only Anthropic*Client.cs or *Llm.cs may reference the Anthropic SDK (ADR-0011). Offending files: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Anthropic_SDK_is_not_referenced_by_scoring_or_the_catalog_data()
    {
        var api = Assembly.Load("SchoolSearch.Api");
        var catalog = Assembly.Load("SchoolSearch.Catalog.Data");

        var scoring = Types.InAssembly(api)
            .That()
            .ResideInNamespace(ScoringNamespace)
            .ShouldNot()
            .HaveDependencyOn(AnthropicNamespace)
            .GetResult();

        var data = Types.InAssembly(catalog)
            .ShouldNot()
            .HaveDependencyOn(AnthropicNamespace)
            .GetResult();

        Assert.True(scoring.IsSuccessful, "Api.Scoring must not reference the Anthropic SDK (B1). Offending types: " + Describe(scoring.FailingTypes));
        Assert.True(data.IsSuccessful, "Catalog.Data must not reference the Anthropic SDK (B1). Offending types: " + Describe(data.FailingTypes));
    }

    [Fact]
    public void Scoring_and_Catalog_Data_have_no_dependency_on_any_AI_namespace()
    {
        var aiNamespaces = AiNamespaces();

        // Non-vacuity: the Api's AI namespace is among them.
        Assert.Contains(ApiAiNamespace, aiNamespaces);

        var api = Assembly.Load("SchoolSearch.Api");
        var catalog = Assembly.Load("SchoolSearch.Catalog.Data");

        var offenders = new List<string>();
        foreach (var ns in aiNamespaces)
        {
            var scoring = Types.InAssembly(api)
                .That()
                .ResideInNamespace(ScoringNamespace)
                .ShouldNot()
                .HaveDependencyOn(ns)
                .GetResult();

            if (!scoring.IsSuccessful)
            {
                offenders.Add($"{ScoringNamespace} -> {ns}: {Describe(scoring.FailingTypes)}");
            }

            var data = Types.InAssembly(catalog)
                .ShouldNot()
                .HaveDependencyOn(ns)
                .GetResult();

            if (!data.IsSuccessful)
            {
                offenders.Add($"{CatalogDataNamespace} -> {ns}: {Describe(data.FailingTypes)}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Import_AI_and_extractor_namespaces_never_reach_the_catalog_database()
    {
        // Extractors read a saved document and write a candidate file (ADR-0017). They never open a DbContext (B8).
        var import = Assembly.Load("SchoolSearch.Import");
        var extractors = import.GetTypes()
            .Where(t => t.Namespace is not null && ImportExtractNamespace.IsMatch(t.Namespace))
            .Select(t => t.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Non-vacuity: the Import AI folder holds the shared LLM interface.
        Assert.Contains("SchoolSearch.Import.Ai", extractors);

        var offenders = new List<string>();
        foreach (var ns in extractors)
        {
            var catalog = Types.InAssembly(import)
                .That()
                .ResideInNamespace(ns)
                .ShouldNot()
                .HaveDependencyOn(CatalogDataNamespace)
                .GetResult();

            if (!catalog.IsSuccessful)
            {
                offenders.Add($"{ns} -> {CatalogDataNamespace}: {Describe(catalog.FailingTypes)}");
            }

            var ef = Types.InAssembly(import)
                .That()
                .ResideInNamespace(ns)
                .ShouldNot()
                .HaveDependencyOn(EntityFrameworkNamespace)
                .GetResult();

            if (!ef.IsSuccessful)
            {
                offenders.Add($"{ns} -> {EntityFrameworkNamespace}: {Describe(ef.FailingTypes)}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Mcp_agent_namespace_has_no_dependency_on_Entity_Framework()
    {
        // The agent lives in the MCP service, which owns no database (ADR-0006). It calls the read-only tool classes.
        var mcp = TryLoad("SchoolSearch.Mcp");
        if (mcp is null)
        {
            return;
        }

        var result = Types.InAssembly(mcp)
            .That()
            .ResideInNamespace(McpAgentNamespace)
            .ShouldNot()
            .HaveDependencyOn(EntityFrameworkNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Mcp.Agent must not reference Entity Framework (ADR-0006, ADR-0017). Offending types: "
            + Describe(result.FailingTypes));
    }

    [Fact]
    public void No_AI_method_named_like_a_state_change_takes_a_DbContext()
    {
        // A model answer may describe a record but never write one. The write paths are the moderation and billing
        // endpoints, which the model cannot reach. This rule catches a helper that would make one reachable.
        var offenders = new List<string>();
        foreach (var assembly in SourceAssemblies())
        {
            foreach (var type in AiTypes(assembly))
            {
                foreach (var method in type.GetMethods(DeclaredMembers))
                {
                    if (!MutatorMethodName.IsMatch(method.Name))
                    {
                        continue;
                    }

                    foreach (var parameter in method.GetParameters())
                    {
                        if (IsDbContext(parameter.ParameterType))
                        {
                            offenders.Add($"{type.FullName}.{method.Name}({parameter.ParameterType.Name})");
                        }
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ai_types_are_scanned_so_the_DbContext_rule_is_not_vacuous()
    {
        var api = Assembly.Load("SchoolSearch.Api");

        Assert.NotEmpty(AiTypes(api).ToArray());
    }

    private static IEnumerable<Type> AiTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace is not null && AiNamespace.IsMatch(type.Namespace))
            {
                yield return type;
            }
        }
    }

    /// <summary>The AI namespaces of every deployable this test run loads (Api, Reviews, Billing, Import, Mcp).</summary>
    private static List<string> AiNamespaces()
    {
        var names = new List<string>();
        foreach (var assembly in SourceAssemblies())
        {
            foreach (var type in AiTypes(assembly))
            {
                if (type.Namespace is not null && !names.Contains(type.Namespace))
                {
                    names.Add(type.Namespace);
                }
            }
        }

        return names;
    }

    private static List<Assembly> SourceAssemblies()
    {
        var loaded = new List<Assembly>();
        foreach (var name in new[] { "SchoolSearch.Api", "SchoolSearch.Reviews", "SchoolSearch.Billing", "SchoolSearch.Import", "SchoolSearch.Mcp" })
        {
            var assembly = TryLoad(name);
            if (assembly is not null)
            {
                loaded.Add(assembly);
            }
        }

        return loaded;
    }

    /// <summary>True when the type or one of its base types is named DbContext (no compile-time EF reference needed).</summary>
    private static bool IsDbContext(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a source file uses the SDK namespace outside a comment.</summary>
    private static bool ReferencesAnthropic(string[] lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("/*", StringComparison.Ordinal) || line.StartsWith('*'))
            {
                continue;
            }

            if (AnthropicUsing.IsMatch(raw) || AnthropicQualified.IsMatch(raw))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(separator + "bin" + separator, StringComparison.Ordinal)
                        && !f.Contains(separator + "obj" + separator, StringComparison.Ordinal));
    }

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
