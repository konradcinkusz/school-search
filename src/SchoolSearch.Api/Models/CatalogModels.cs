using System.ComponentModel.DataAnnotations;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Models;

/// <summary>Marker for request objects the <see cref="Filters.ValidationFilter"/> validates.</summary>
public interface IValidatedRequest
{
}

public static class Methodology
{
    /// <summary>Methodology version served by /api/v1/meta. Draft until Phase B.</summary>
    public const string Version = "0.0-draft";
}

public sealed record ProvenanceDto(string Source, string SourceUrl, DateOnly AsOf, string Method);

/// <summary>
/// Public school view. The governing body is exposed as a type code only; no governing body name exists in the model.
/// </summary>
public sealed record SchoolDto(
    long IdPodmiot,
    string Name,
    string Slug,
    string? GminaTeryt,
    int? TypeOfGoverningBody,
    ProvenanceDto Provenance)
{
    public static SchoolDto From(School school) => new(
        school.IdPodmiot,
        school.Name,
        school.Slug,
        school.GminaTeryt,
        school.TypeOfGoverningBody,
        new ProvenanceDto(
            school.Provenance.Source,
            school.Provenance.SourceUrl,
            school.Provenance.AsOf,
            school.Provenance.Method));
}

public sealed record SchoolPage(IReadOnlyList<SchoolDto> Items, int Page, int Limit, int Total);

public sealed record MetaResponse(string MethodologyVersion, DateOnly? DataVintage);

/// <summary>Route binding for /api/v1/schools/{slug}. Slugs are lowercase ASCII words joined by hyphens.</summary>
public sealed record SchoolSlugRequest(
    [property: Required]
    [property: StringLength(120, MinimumLength = 1)]
    [property: RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug must be lowercase letters, digits and hyphens.")]
    string Slug) : IValidatedRequest;
