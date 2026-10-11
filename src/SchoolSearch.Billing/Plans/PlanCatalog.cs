using System.Text.RegularExpressions;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.Plans;

/// <summary>A validated plan. Plans are server-side: the client sends a code, never an amount.</summary>
public sealed record PlanDefinition(string Code, string Name, decimal? PriceGrossPln, string Interval);

/// <summary>
/// The plan catalogue from Billing:Plans. Built once at startup; invalid or duplicate entries throw, so the service
/// fails to start instead of selling something misconfigured. An empty catalogue is valid (pricing page says "in preparation").
/// </summary>
public sealed class PlanCatalog
{
    private static readonly Regex CodePattern = new(@"^[a-z0-9][a-z0-9-]{0,49}$", RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<PlanDefinition> _plans;

    private PlanCatalog(IReadOnlyList<PlanDefinition> plans)
    {
        _plans = plans;
    }

    public IReadOnlyList<PlanDefinition> Plans => _plans;

    public static PlanCatalog FromOptions(IEnumerable<PlanOptions> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var plans = new List<PlanDefinition>();
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in source)
        {
            var code = option.Code?.Trim() ?? string.Empty;
            if (!CodePattern.IsMatch(code))
            {
                throw new InvalidOperationException(
                    "Billing:Plans entry has an invalid code. Use 1 to 50 lower-case letters, digits or hyphens.");
            }

            if (!codes.Add(code))
            {
                throw new InvalidOperationException($"Billing:Plans contains the duplicate code '{code}'.");
            }

            var name = option.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 120)
            {
                throw new InvalidOperationException($"Billing:Plans entry '{code}' needs a name of 1 to 120 characters.");
            }

            if (option.Interval is not (PlanIntervals.Month or PlanIntervals.Year))
            {
                throw new InvalidOperationException($"Billing:Plans entry '{code}' has an interval other than 'month' or 'year'.");
            }

            if (option.PriceGrossPln is < 0m)
            {
                throw new InvalidOperationException($"Billing:Plans entry '{code}' has a negative price.");
            }

            plans.Add(new PlanDefinition(code, name, option.PriceGrossPln, option.Interval));
        }

        return new PlanCatalog(plans);
    }

    /// <summary>The plan with this code, or null when the catalogue has none.</summary>
    public PlanDefinition? Find(string code) =>
        _plans.FirstOrDefault(plan => string.Equals(plan.Code, code, StringComparison.Ordinal));
}
