# ADR-0014: Preschools have no quality score

- **Status:** Accepted
- **Date:** 2026-10-11
- **Deciders:** owner (Konrad Cinkusz), to confirm at Phase D review; the Phase D brief states the rule (preschools get no quality score)

## Context

Phase D adds preschools (przedszkola) to the catalog. Schools (Phase B and C) carry a value-added score with an
interval, cohort size, exam year and methodology version (MASTER-PROMPT §4, ADR-0009). The score depends on exam
results, and preschools have no exam. Any number derived for a preschool would be a judgement without a basis.

What parents need from a preschool is practical: where it is, whether it is public, what it costs per month
(czesne), when it opens and closes, and whether there are free places. These facts come from public sources, and
they change, so each one has to carry its own source and date.

The risk is not theoretical. A rank, a grade or a "best" label is easy to add to a list page, to a sort option or
to an MCP reply, and once a parent sees one it reads as an official judgement. The product must not make that
judgement, and the rule has to hold across the database, the API, the MCP server and the web UI.

## Decision

1. **A preschool never has a quality score, rank, grade, rating, "best" or "worst" label, or any numeric or ordinal
   judgement derived from its data.** This applies to every layer: the database (`Preschool`, `PreschoolFacts`), the
   public contract (`Preschool*` types in `SchoolSearch.Contracts.Public`), the API, the MCP tools and resources, and
   the web UI.
2. **A preschool shows practical facts only:** location, public or non-public, the monthly tuition range (czesne, PLN),
   optional monthly meal price, opening hours, free places with an as-of date, and the type of its governing body.
3. **Every fact carries provenance:** `source`, `sourceUrl`, `asOf`, `method` and `confidence`. `confidence` is
   `reported` (from a primary public source, loaded from a file) or `unverified` (submitted by an operator or parent
   and not checked). No value is invented. An unknown value stays null, and the UI says "brak danych".
4. **The governing body's name is never stored.** Only `GoverningBodyTypeCode` is kept, under the same rule as schools:
   private-individual codes (141, 146, 150) yield the type only.
5. **No child or parent personal data** in any preschool table, file or response. `PersonalDataGuard` applies to every
   new CSV loader.
6. **Sources are files the operator loads.** The SIO preschool units (CC0 dataset), and the municipal (gmina) vacancy
   lists and tuition tables that the operator supplies as CSV. The product does not scrape competitors or fetch
   operator pages (MASTER-PROMPT, SOURCES §4).
7. **The sort order is alphabetical by name** (`sort=name`, the default). It is not a quality order and is not
   described as one.
8. **Fixed notice.** Every preschool profile carries the same notice: "Przedszkola nie mają wyniku jakości. Pokazujemy
   praktyczne informacje: lokalizację, opłaty, godziny i wolne miejsca, każdą ze źródłem i datą." It states the rule
   to the reader, not only to the developer.

## Options considered

| Option | For | Against |
|---|---|---|
| **No score; facts with provenance (chosen)** | Matches what the data can support. Parents get the facts they need to decide. Nothing to misread as a judgement. | Less to compare at a glance. The product gives no answer to "which is best". |
| Score from free-place counts or tuition | A single number per preschool, easy to sort. | A number with no methodology, no cohort and no interval. Price and vacancy are not quality. Breaks METRIC-ETHICS §3 (no number without its confidence). |
| Score with a "not yet published" flag, shown when enough data exists | Looks like the school model. | There is no quality measure to validate against, so any formula is invented. The flag would not stop the judgement from being read. |
| Show a score for internal use only, hide it in the UI | Keeps a number for later. | Still stored and still reachable through the API or MCP. A hidden number leaks. |

## Enforcement

- **Architecture test** `tests/SchoolSearch.Architecture.Tests/PreschoolNoScoreTests.cs`: no property or column on
  `Preschool`, `PreschoolFacts`, or any type in `SchoolSearch.Contracts.Public` whose name starts with `Preschool` may
  have a name containing `score`, `rank`, `rating`, `grade`, `quality`, `best` or `worst` (case-insensitive). These
  types must not reference `SchoolSearch.Api.Scoring`.
- **Protocol tests** `tests/SchoolSearch.Mcp.Tests/McpPreschoolTests.cs`: no key in a preschool reply matches that
  pattern, and both preschool tool descriptions state the rule.
- **API tests** (`tests/SchoolSearch.Api.Tests/Preschools/**`): the JSON of a profile and of a list item contains no
  such key, and the OpenAPI document has no `sort` or `order` parameter other than `sort`.
- **Review**: any change that adds such a field must be rejected at review. A test that is deleted to make a change pass
  is a review failure.

## Consequences

- The product cannot answer "which preschool is best". The UI, the MCP tool descriptions and the guided prompts say
  so, and point to the facts and their sources.
- Fact freshness is visible. A free-places count shows its as-of date, and an old value is shown as old, not hidden.
- Confidence is visible on each fact. An `unverified` value is shown with that label, never as checked.
- An AI-assisted extraction of tuition tables is designed in `docs/ai/AI-FEATURES.md` (feature 6). It proposes rows
  for a human to verify. It does not write to the database, and it has no field for quality (ADR-0011 B1 and B2).
- If a future owner decision wants a judgement for preschools, that is a new ADR. It must name the data, the method,
  the cohort rule and the interval, and it must pass METRIC-ETHICS review. It cannot be added as a field.
