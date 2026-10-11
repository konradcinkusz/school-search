using System.ComponentModel.DataAnnotations;

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

public sealed record MetaResponse(string MethodologyVersion, DateOnly? DataVintage);

/// <summary>Route binding for /api/v1/schools/{slug}. Slugs are lowercase ASCII words joined by hyphens.</summary>
public sealed record SchoolSlugRequest(
    [property: Required]
    [property: StringLength(120, MinimumLength = 1)]
    [property: RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug must be lowercase letters, digits and hyphens.")]
    string Slug) : IValidatedRequest;
