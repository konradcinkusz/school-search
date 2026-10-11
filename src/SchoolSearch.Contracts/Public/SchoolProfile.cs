namespace SchoolSearch.Contracts.Public;

/// <summary>
/// Full public view of one school (<c>GET /api/v1/schools/{slug}</c>). The governing body is exposed as a
/// type only; no operator name exists in this contract.
/// </summary>
public sealed record SchoolProfile(
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    bool IsSpecial,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    string ScoreStatus,
    ScoreBlock? Score,
    GoverningBodyInfo GoverningBody,
    FaithInfo Faith,
    IReadOnlyList<CohortRow> Cohorts,
    IReadOnlyList<TrendPoint> Trend,
    IReadOnlyList<ProvenanceRow> Provenance,
    string MethodologyVersion);

/// <summary>Governing body as a type. The name of the operator is never stored or returned.</summary>
/// <param name="TypeCode">idTypOrgProw code, or null when unknown.</param>
/// <param name="TypeLabel">Label of the type, or null when none is known.</param>
/// <param name="LabelVerified">False while the label is unverified against the source dictionary.</param>
/// <param name="IsNaturalPerson">True when the governing body is a private individual (type level only).</param>
public sealed record GoverningBodyInfo(
    int? TypeCode,
    string? TypeLabel,
    bool LabelVerified,
    bool IsNaturalPerson);

/// <summary>Faith affiliation. Derived only from the governing body type, never from a school name or patron.</summary>
public sealed record FaithInfo(bool Derived, bool? Declared, string Note);

/// <summary>Pupil and division counts for one school year and class level.</summary>
public sealed record CohortRow(int SchoolYear, string ClassLevel, int Pupils, int Oddzialy);

/// <summary>One exam year of a school's history.</summary>
public sealed record TrendPoint(
    int ExamYear,
    string ScoreStatus,
    ScoreBlock? Score,
    double? CoveragePercent,
    IReadOnlyList<SubjectRow> Subjects);

/// <summary>A school's mean result in one subject for one exam year.</summary>
public sealed record SubjectRow(string Subject, double MeanPercent, int PupilsSat);

/// <summary>Provenance of one published field group. <see cref="AsOf"/> is an ISO date (yyyy-MM-dd).</summary>
public sealed record ProvenanceRow(string Field, string Source, string SourceUrl, string AsOf, string Method);
