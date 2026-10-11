namespace SchoolSearch.Contracts;

/// <summary>Response of <c>GET /api/v1/meta</c>: the methodology and data vintage the API is serving.</summary>
/// <param name="MethodologyVersion">The methodology version in force, for example "0.0-draft".</param>
/// <param name="DataVintage">The data vintage, or null until an import has run.</param>
public sealed record MetaDto(string MethodologyVersion, DateOnly? DataVintage);
