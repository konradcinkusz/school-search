namespace SchoolSearch.Contracts;

/// <summary>
/// One school in a list. Score and Provenance are null until the scoring engine (Phase B) publishes them.
/// Only schools are represented: there is no teacher or pupil shape in this contract.
/// </summary>
/// <param name="IdPodmiot">The SIO entity identifier (idPodmiot). Stability is an open question recorded in docs/data/SOURCES.md.</param>
/// <param name="Name">The public name of the school.</param>
/// <param name="Slug">The URL slug.</param>
/// <param name="Gmina">The gmina (municipality) name, when known.</param>
/// <param name="Score">The published score, or null when none is published.</param>
/// <param name="Provenance">The provenance of the published values, or null when none is published.</param>
public sealed record SchoolSummaryDto(
    long IdPodmiot,
    string Name,
    string Slug,
    string? Gmina,
    ScoreDto? Score,
    Provenance? Provenance);
