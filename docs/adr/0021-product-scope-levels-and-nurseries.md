# ADR-0021: Product scope in levels; nurseries as a facility kind; second edition deferred

- **Status:** Proposed (the owner decides; nothing in this ADR is in force until the owner marks it Accepted)
- **Date:** 2026-10-14
- **Deciders:** owner (Konrad Cinkusz), not yet decided. Drafted by an agent at the owner's request, with the author's recommendation recorded in Decision.

## Context

**What the product is today.** `docs/MASTER-PROMPT.md` describes a Polish portal for primary-school search and ranking,
with a hosted MCP server. Primary schools carry a value-added score with an interval, cohort and `methodologyVersion`
(ADR-0009, `docs/methodology/RANKING-METHODOLOGY.md`). Preschools (przedszkola) are listed with practical facts only and
no quality score (ADR-0014). The preschool layer is currently built as a separate family of types and endpoints:
`Preschool*` in `src/SchoolSearch.Contracts/Public/`, `src/SchoolSearch.Api/Preschools/PreschoolEndpoints.cs`,
`src/SchoolSearch.Mcp/Tools/PreschoolTools.cs`, `src/SchoolSearch.Api/Demo/DemoPreschoolSeed.cs`.

**What the owner is considering.**

- (a) Adding nurseries (żłobki, children aged 0 to 3) next to preschools and primary schools.
- (b) A second edition for future secondary-school applicants, secondary students and students (liceum, technikum,
  szkoła branżowa, then universities).

**Constraints that shape the answer.**

- The product brief forbids invented data, legal bases and numbers. Any unknown is recorded as OPEN in
  `docs/data/SOURCES.md` with how to verify it (AGENTS.md, MASTER-PROMPT Ground rules). Nothing in this ADR states a
  coverage count, a licence term or a register format as a fact.
- The anti-goals hold for every level: no teacher or pupil entity, no ascending sort on a composite, no nationality data,
  no people-heuristics, no score below the threshold, and every published value carries `source`, `sourceUrl`, `asOf`,
  `method` (MASTER-PROMPT, Anti-goals; RANKING-METHODOLOGY §2).
- The METRIC-ETHICS gate applies to any level that publishes a number about a school. The full guide lives in the
  `architecture-standards` repository, not in this one. For this ADR the rules were taken from the condensed copy in
  MASTER-PROMPT and from the gate checklist in `.github/agents/scoring-ethics-reviewer.md`. **The guide itself was not
  read when this ADR was drafted.** Its section numbers are cited here as the repository cites them, not re-checked.
- Phases A to F in MASTER-PROMPT are the current plan. Phase D (preschools) is the nearest precedent for a level with
  no score. Phase E brings user accounts (ADR-0004, ADR-0015). Phase F brings monetisation.
- Users of the second edition would be minors (secondary students) and young adults (students). That changes the consent,
  age-gating and privacy posture. No such posture exists in this repository today, and none is proposed here.

**What is not known.** The publisher, format, licence and access route of the national register of nurseries and
children's clubs; whether municipal fee and vacancy lists exist in usable form for nurseries; whether RSPO covers
nursery-age facilities; the RSPO API response itself (SOURCES §2, still blocked from the build environment); and the
availability and licence of CKE matura results at school level and of graduate-outcome data for universities. All are
OPEN in `docs/data/SOURCES.md` (section 4B, added with this ADR).

## Decision

**Recommendation (the author's view, for the owner to accept or reject).**

1. **Focus now on three levels for parents, in one product:**
   - **Level 1 - childcare: nurseries (żłobki) and preschools (przedszkola).** Practical facts only: location, public or
     non-public, fees, opening hours, free places, each with `source`, `sourceUrl`, `asOf`, `method` and `confidence`.
     **No quality score, rank, grade or "best" label** (ADR-0014 applies in full). Nurseries are a new **facility kind**
     (`kind = nursery`) inside the same facts model that preschools use. They are not a new product, a new database or
     a new score.
   - **Level 2 - primary schools (szkoły podstawowe).** Scores with interval, cohort size, year and `methodologyVersion`,
     as today (ADR-0009, RANKING-METHODOLOGY).
   - Levels are a dimension of the data, shown to the parent as a choice of "stage of schooling", not as separate sites.
2. **Defer the second edition** (secondary, students, universities). It is not started, not scoped for build, and no
   data source for it is licensed or verified. The reasons are in Context and Options: a different audience, different
   sources and licences, much higher ranking pressure, a different METRIC-ETHICS review, a different legal and privacy
   posture (minors, age gating, consent), and different UX (profiles and extended subjects, thresholds, admission points).
   Liceum rankings and matura and ELA outcome figures are among the most gamed public numbers, so the anti-ranking rules
   would have to be re-argued there rather than inherited.
3. **Keep the architecture ready without building the second edition:**
   - **Catalog contract:** a facility or school `kind` and an `educationLevel` dimension, both as extensible enums with an
     `unknown` fallback, so a new value is additive. Clients render an unknown value generically and never drop it.
   - **Map and AI-filter schema:** filter by `kind` and `educationLevel`, not by hard-coded type names. The AI filter
     (`docs/ai/AI-FEATURES.md`) reads the same enum and does not gain a second vocabulary.
   - **Scoring kernel stays free of entity types.** It takes plain values (RANKING-METHODOLOGY §3) and knows neither
     `School`, nor `Nursery`, nor `Preschool`. The architecture test that keeps billing and user types out of Scoring
     stays in place.
   - **Methodology versioned per level.** Primary stays at its own version. A level that publishes no score has no
     methodology version, and says so. A future secondary level gets its own methodology document and version, not a
     branch of the primary one.
   - **Facility kind before any nursery data:** when nurseries start, the `Preschool*` family is generalised to a
     childcare facility with `kind`, and the ADR-0014 architecture test follows the renamed types. The timing of that
     rename is an owner decision (see Open decisions). Until a nursery source is verified, nothing about nurseries is
     built.
4. **Sequencing.**
   - Step 1: this ADR is decided by the owner.
   - Step 2: verify the OPEN items for Level 1 nurseries in SOURCES section 4B. Nothing is loaded before the source,
     licence and format are recorded.
   - Step 3: primary edition goes live with real data (SOURCES section 6 items 1 to 7 closed, Phase B gate reproduced).
   - Step 4: nurseries join Level 1 as `kind = nursery` for the gminas whose data is verified. Coverage is stated per
     gmina, never assumed.
   - Step 5: second edition, only when the trigger below is met.
5. **Trigger to start the second edition.** All of the following, each recorded in an ADR or in SOURCES:
   - the primary edition is live with real data, and the owner has signed off the primary methodology (ADR-0009 open
     points and RANKING-METHODOLOGY §10 closed or explicitly accepted);
   - the source for each secondary or university figure is verified, with its format, licence and suppression rule
     recorded (SOURCES section 4B);
   - a METRIC-ETHICS review of the proposed secondary or matura methodology has passed, and it states what the level will
     **not** publish (in particular, no ordinal ranking of schools by outcome);
   - a privacy and age-gating posture for users under 18 is written and reviewed, and the owner has decided whether
     minors may use accounts at all (ADR-0004 and the Phase E auth model apply first);
   - the owner has decided the product and brand question (see Open decisions).

## Options considered

| Option | For | Against |
|---|---|---|
| **A. One product, levels as a data dimension (recommended)** | One domain, one catalog, one map, one set of anti-goal tests. Parents see the stages of schooling in one place. The second edition can be added as a level later without a second deployment. | One product carries several audiences, so copy and UX must make the level explicit. A single brand must survive a later, more sensitive level. |
| B. Two products, separate deployments and codebases (childcare and primary; secondary and students) | Each product fits its audience. The second edition can have its own legal and privacy posture from day one. | Duplicated kernel, catalog, import and anti-goal tests. Two sets of sources to keep current. Cost and maintenance grow for a small team. |
| C. Two brands on one platform (shared kernel, infrastructure and data layer; separate brand, domain, copy and audience) | Keeps the sensitive later edition away from the parent brand. Reuses the platform. | Requires the platform to stay strictly entity-free, which must be enforced. Two brands are two promises to keep. Premature until the second edition is real. |
| D. Build everything now (nurseries, primary, secondary, universities) | Covers the whole path in one release. | Breaks the sequencing above: three unverified source sets at once, a ranking-pressure problem the primary methodology has not yet proven against, and a minors' privacy posture that does not exist. Violates the rule that an unknown is OPEN, not built. |
| E. Nurseries as a separate product | Clear audience for 0 to 3. | Repeats the infrastructure of option B for a level that has no score. Nurseries share the facts model with preschools (ADR-0014), so separating them duplicates it. |

## Consequences

- **Easier:** the current scope stays small and honest. The three-level structure gives parents a clear path from
  nursery to primary school. Facility kind and education level are in place before any new level needs them, so adding
  one is additive. The second edition has a written trigger, so it cannot start on enthusiasm alone.
- **Harder:** the catalog contract, the map and the AI filter must carry a `kind` and an `educationLevel` value. The
  copy has to say which level a page is about. A rename of `Preschool*` to a childcare kind will touch the ADR-0014
  architecture test, the MCP tool descriptions and the demo seed, and must be done in one reviewed change.
- **Risks:**
  - Nursery data is likely to be municipal and uneven. Coverage will be per gmina and will look patchy. This must be
    shown, not hidden (the same rule as catchment in ADR-0018).
  - Free-place and fee values change often. Each one shows its as-of date, and an old value is shown as old.
  - A nursery "best" label is as tempting as a preschool one, and ADR-0014 forbids it. The same tests must cover nurseries.
- **What this ADR does not decide:** any nursery or secondary coverage figure, any licence, any register format, any
  pricing, any legal basis for processing minors' data, and any date.
- **Exit condition:** this ADR is a scope decision, not a temporary one. It is revisited when the owner accepts, rejects
  or amends it, or when the trigger for the second edition is met.

## Open decisions for the owner

1. **Accept, amend or reject** the three-level scope and the deferral of the second edition.
2. **Nurseries before verification.** Confirm that no nursery facts are published until the register or the gmina
   sources are verified (recommended: yes, no exception).
3. **Rename timing.** Generalise `Preschool*` to a childcare facility kind now, or when nurseries are first loaded
   (recommended: when nurseries are first loaded, in one reviewed change).
4. **Product and brand.** Option A, B or C for the second edition, and whether the current brand may carry it.
5. **Minors.** Whether anyone under 18 may use accounts or submit data at all, and on what consent basis. No answer is
   assumed here.
6. **What replaces ranking for secondary schools.** The second edition may publish profiles and facts, not an ordinal
   list of liceum by outcome. Confirm this anti-goal before the trigger is considered.
7. **Methodology sign-off per level.** Who signs off the primary methodology, and who signs off any future level.
8. **Operator-submitted data.** Whether parent or operator-submitted fee and vacancy values (confidence `unverified`,
   ADR-0014) are acceptable for nurseries, or only officially published values.
