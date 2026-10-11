# ADR-0010: Geocoding source (DECISION PENDING, Phase B)

- **Status:** Pending (decision not yet taken; no provider is chosen)
- **Date:** 2026-10-11

## Context

The wedge is address to catchment school (obwód). Turning a user's address into coordinates or a TERYT gmina code
needs a geocoder. The brief lists "geocoder licence" as unknown and requires that nothing be guessed. Catchment
availability is also unknown per gmina.

## Decision

Pending. Phase B may not ship address search, distance or catchment lookup until this ADR is Accepted.

## What must be verified before choosing

1. **Licence and terms:** whether results may be stored, cached and displayed commercially; attribution text;
   whether the licence permits derived data (for example, a stored coordinate per school).
2. **Usage policy and limits:** rate limits, batch limits, caching rules and any requirement to show the provider.
3. **Coverage:** Polish addresses, including rural addresses without a street number, and the precision of the result.
4. **Join key:** whether the result carries a TERYT (gmina) code that joins to catchment data.
5. **Cost and availability:** price at the expected query volume, and what happens when the provider is down
   (degrade per P8: the search must still work with a clear state, not fail).
6. **Candidates to compare, unverified here:** official national address data, an open-data geocoder under its own
   usage policy, and a commercial geocoder. None is confirmed as suitable.

Do not evaluate a provider by scraping a competitor's site or by using a service whose terms forbid storage.

## Options considered

Not decided. The candidates above are a list to check, not a shortlist.

## Consequences

Until Accepted, the search surface uses school-level data only (no address input), and the UI does not promise
catchment results. The record of the verified answers to the six questions above is added to this ADR when
Phase B decides, and the choice is linked from `docs/data/SOURCES.md`.
