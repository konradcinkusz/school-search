namespace SchoolSearch.Contracts.Public;

/// <summary>
/// Practical facts of one preschool. Every fact group is optional: a null value means the fact is unknown, never zero.
/// <see cref="Source"/> applies to all facts of the block.
/// </summary>
/// <param name="MonthlyTuition">Monthly tuition (czesne) range in PLN. Both bounds null means unknown.</param>
/// <param name="MonthlyMealPln">Monthly meal fee (wyżywienie) in PLN, or null when unknown.</param>
/// <param name="Hours">Opening and closing time, "HH:mm", or null when unknown.</param>
/// <param name="FreePlaces">Vacancies (wolne miejsca) and the date they describe.</param>
/// <param name="Source">Provenance of the facts and their confidence.</param>
public sealed record PreschoolFactsBlock(
    MoneyRange MonthlyTuition,
    decimal? MonthlyMealPln,
    OpeningHours Hours,
    FreePlaces FreePlaces,
    FactSource Source);

/// <summary>Where a fact comes from. <see cref="AsOf"/> is serialised as an ISO date (yyyy-MM-dd).</summary>
/// <param name="Confidence">"reported" (primary public source, loaded from file) or "unverified" (submitted, not checked).</param>
public sealed record FactSource(string Source, string SourceUrl, DateOnly AsOf, string Method, string Confidence);

/// <summary>Monthly amount range in PLN. Both null means unknown.</summary>
public sealed record MoneyRange(decimal? MinPln, decimal? MaxPln);

/// <summary>Opening hours as "HH:mm", or null when unknown.</summary>
public sealed record OpeningHours(string? Opens, string? Closes);

/// <summary>Number of free places and the date it describes. Count 0 means no vacancies were reported.</summary>
public sealed record FreePlaces(int? Count, DateOnly? AsOf);
