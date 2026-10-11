namespace SchoolSearch.Contracts.Public;

/// <summary>Response of <c>GET /api/v1/methodology</c>: the parameters the published scores are computed with.</summary>
public sealed record MethodologyInfo(
    string Version,
    int PublicationThreshold,
    double CoverageFloor,
    string Weights,
    double IntervalZ,
    string Description,
    string DocsUrl);
