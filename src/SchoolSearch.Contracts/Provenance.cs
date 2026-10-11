namespace SchoolSearch.Contracts;

/// <summary>
/// Every published value carries its provenance: where it came from, the link to it, the date it
/// describes, and how it was derived.
/// </summary>
/// <param name="Source">Human-readable source name, for example "SIO Szkoly_2025".</param>
/// <param name="SourceUrl">Link to the source.</param>
/// <param name="AsOf">The date the value describes.</param>
/// <param name="Method">How the value was derived.</param>
public sealed record Provenance(string Source, string SourceUrl, DateOnly AsOf, string Method);
