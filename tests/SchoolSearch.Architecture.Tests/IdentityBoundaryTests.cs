using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace SchoolSearch.Architecture.Tests;

/// <summary>
/// Identity boundary (ADR-0015): authservice is the only identity provider and the only holder of a signing key.
/// Source assemblies validate its tokens; they never create tokens, store users or hash passwords.
/// </summary>
public sealed class IdentityBoundaryTests
{
    private const string AspNetCoreIdentity = "Microsoft.AspNetCore.Identity";
    private const string SecurityTokenDescriptor = "Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor";
    private const string IdentityTokensNamespace = "Microsoft.IdentityModel.Tokens";
    private const string ReviewsNamespace = "SchoolSearch.Reviews";
    private const string ScoringNamespace = "SchoolSearch.Api.Scoring";

    // SchoolSearch.Reviews and SchoolSearch.Billing are referenced only once their projects exist (see the csproj); each is loaded when present.
    private static readonly string[] SourceAssemblyNames =
    {
        "SchoolSearch.Api",
        "SchoolSearch.Catalog.Data",
        "SchoolSearch.ServiceDefaults",
        "SchoolSearch.Contracts",
        "SchoolSearch.Import",
        ReviewsNamespace,
        // Phase F (ADR-0016): Billing validates authservice tokens too. It is loaded only when its project exists.
        "SchoolSearch.Billing",
        // Phase G (ADR-0017): the MCP service hosts the agent. It validates nothing and mints nothing (no tokens, no users).
        "SchoolSearch.Mcp",
    };

    private static readonly string[] ScoreContractTypeNames = { "ScoreBlock", "ScoreDto" };

    [Fact]
    public void Source_assemblies_are_loaded_so_the_identity_rules_are_not_vacuous()
    {
        var loaded = SourceAssemblies();

        Assert.True(loaded.Count >= 5, "Expected at least Api, Catalog.Data, ServiceDefaults, Contracts and Import to load.");
        Assert.All(loaded, assembly => Assert.NotEmpty(assembly.GetTypes()));
    }

    [Fact]
    public void No_source_assembly_depends_on_ASP_NET_Core_Identity()
    {
        var offenders = new List<string>();
        foreach (var assembly in SourceAssemblies())
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(AspNetCoreIdentity)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.Add($"{assembly.GetName().Name}: {Describe(result.FailingTypes)}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_source_project_file_references_an_ASP_NET_Core_Identity_package()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(projects);
        var offenders = projects
            .Where(p => File.ReadAllText(p).Contains("Include=\"Microsoft.AspNetCore.Identity", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFileName(p))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_source_assembly_uses_SecurityTokenDescriptor_so_no_token_is_created_in_this_solution()
    {
        var offenders = new List<string>();
        foreach (var assembly in SourceAssemblies())
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(SecurityTokenDescriptor)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.Add($"{assembly.GetName().Name}: {Describe(result.FailingTypes)}");
            }
        }

        Assert.Empty(offenders);

        // Reflection complement: a signature that exposes the type would not show up as a body dependency.
        foreach (var assembly in SourceAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var referenced in ShapeReferencedTypes(type))
                {
                    Assert.False(
                        referenced.Name == "SecurityTokenDescriptor",
                        $"{type.FullName} exposes {referenced.FullName}, which creates tokens.");
                }
            }
        }
    }

    [Fact]
    public void No_source_type_is_a_user_store_or_a_password_hasher()
    {
        var pattern = new Regex("User.*Store|PasswordHasher", RegexOptions.CultureInvariant);

        var offenders = SourceAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => pattern.IsMatch(t.Name))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Token_validation_is_detected_so_the_identity_rules_are_not_vacuous()
    {
        // The Api validates authservice tokens with TokenValidationParameters. If the engine reports no
        // dependency on the Tokens namespace, the negative rules above would prove nothing.
        var result = Types.InAssembly(ApiAssembly())
            .ShouldNot()
            .HaveDependencyOn(IdentityTokensNamespace)
            .GetResult();

        Assert.False(result.IsSuccessful, "Expected the engine to detect the token validation dependency of the Api.");
    }

    [Fact]
    public void Scoring_namespace_has_no_dependency_on_the_Reviews_service()
    {
        var result = Types.InAssembly(ApiAssembly())
            .That()
            .ResideInNamespace(ScoringNamespace)
            .ShouldNot()
            .HaveDependencyOn(ReviewsNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Api.Scoring must not depend on the Reviews service (reviews never feed scoring). Offending types: "
            + Describe(result.FailingTypes));
    }

    [Fact]
    public void Api_assembly_has_no_dependency_on_the_Reviews_service()
    {
        // Database-per-service: reviews reach the Api only over HTTP, never through a shared assembly.
        var result = Types.InAssembly(ApiAssembly())
            .ShouldNot()
            .HaveDependencyOn(ReviewsNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "The catalog Api must not reference the Reviews service. Offending types: "
            + Describe(result.FailingTypes));
    }

    [Fact]
    public void Score_contract_types_expose_no_member_or_parameter_mentioning_reviews()
    {
        var contracts = SourceAssemblies().Single(a => a.GetName().Name == "SchoolSearch.Contracts");
        var scoreTypes = contracts.GetTypes()
            .Where(t => ScoreContractTypeNames.Contains(t.Name, StringComparer.Ordinal))
            .ToArray();

        // Non-vacuity: both score types must exist, or the check below proves nothing.
        Assert.Equal(ScoreContractTypeNames.Length, scoreTypes.Length);

        var offenders = new List<string>();
        foreach (var type in scoreTypes)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member.Name.Contains("review", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{type.FullName}.{member.Name}");
                }
            }

            foreach (var referenced in ShapeReferencedTypes(type))
            {
                if (referenced.Name.Contains("review", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{type.FullName} -> {referenced.FullName}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static Assembly ApiAssembly() => Assembly.Load("SchoolSearch.Api");

    /// <summary>The source assemblies that are present in this test run. Reviews is absent until its project exists.</summary>
    private static List<Assembly> SourceAssemblies()
    {
        var loaded = new List<Assembly>();
        foreach (var name in SourceAssemblyNames)
        {
            try
            {
                loaded.Add(Assembly.Load(name));
            }
            catch (FileNotFoundException)
            {
                // Not referenced by this build; the rules then cover the assemblies that exist.
            }
        }

        return loaded;
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

    private static IEnumerable<Type> ShapeReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
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

    private static string Describe(IEnumerable<Type>? types) =>
        string.Join(", ", (types ?? Enumerable.Empty<Type>()).Select(t => t.FullName ?? t.Name));
}
