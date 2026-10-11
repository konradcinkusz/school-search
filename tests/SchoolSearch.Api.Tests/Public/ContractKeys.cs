namespace SchoolSearch.Api.Tests.Public;

/// <summary>The exact property sets of the public JSON contract (camelCase), as served by the Phase C read API.</summary>
internal static class ContractKeys
{
    public static readonly string[] SchoolListItem =
    [
        "slug", "name", "gminaTeryt", "gminaName", "isPublic", "isSpecial",
        "latitude", "longitude", "distanceKm", "scoreStatus", "score",
    ];

    public static readonly string[] SchoolProfile =
    [
        "slug", "name", "gminaTeryt", "gminaName", "isPublic", "isSpecial",
        "latitude", "longitude", "distanceKm", "scoreStatus", "score",
        "governingBody", "faith", "cohorts", "trend", "provenance", "methodologyVersion",
    ];

    public static readonly string[] ScoreBlock =
    [
        "score", "reason", "cohortSize", "intervalLow", "intervalHigh",
        "methodologyVersion", "examYear", "asOf", "source",
    ];

    public static readonly string[] SchoolPage = ["items", "page", "pageSize", "total"];

    public static readonly string[] RankingPage =
    [
        "scope", "gminaTeryt", "examYear", "methodologyVersion",
        "items", "unrankedCount", "total", "page", "pageSize",
    ];

    public static readonly string[] RankedItem = ["rank", "slug", "name", "gminaName", "score"];

    public static readonly string[] Methodology =
    [
        "version", "publicationThreshold", "coverageFloor", "weights", "intervalZ", "description", "docsUrl",
    ];

    public static readonly string[] Gmina = ["teryt", "name", "schoolCount", "coverageState"];

    public static readonly string[] Catchment = ["gminaTeryt", "coverage", "match", "schools", "disclaimer"];

    public static readonly string[] CatchmentCoverage = ["state", "resolutionUrl", "resolutionDate"];
}
