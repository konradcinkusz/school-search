namespace SchoolSearch.Contracts.Public;

/// <summary>A gmina (municipality) with its school count and catchment-data coverage.</summary>
/// <param name="Teryt">TERYT code of the gmina.</param>
/// <param name="Name">Gmina name, when known.</param>
/// <param name="SchoolCount">Number of schools in the gmina.</param>
/// <param name="CoverageState">"none", "street_list" or "polygon".</param>
public sealed record GminaInfo(string Teryt, string? Name, int SchoolCount, string CoverageState);
