# ADR-0018: Catchment reports "street listed, number not covered" as `no_number_match`

- **Status:** Accepted (the author-decided implementation; open point 1 is for the product owner)
- **Date:** 2026-10-12
- **Deciders:** author of the change (Konrad Cinkusz), for the implementation as built. Open point 1 belongs to product.

## Context

`GET /api/v1/catchment?gmina=&street=&number=` (`src/SchoolSearch.Api/Catchment/CatchmentEndpoints.cs`) answers
which school's catchment (obwód) covers an address, using the street list that the gmina published in its resolution.
It never derives a boundary from distance. Before this change the endpoint returned `no_match` in two different cases:

1. the street is not in the gmina's street list at all;
2. the street is in the list, but none of its rules covers the house number (a range or parity that does not fit).

Both cases gave the same answer, so the web page showed the same message for both: the street was not found. A parent
who typed a real street with a number outside the listed ranges was told that the street does not exist. That is
false, and it sends the parent to check spelling when the problem is the number or the resolution's ranges.

The fix has to separate the two cases in the API. The MCP tool and the web page then have to say the right thing.
The constraints are: the catchment answer is a public contract (`CatchmentResponse.Match` is a string that clients
branch on), and the product must not invent a school or a boundary (the endpoint returns no school for any non-match).

## Decision

1. **A new match value `no_number_match`.** The endpoint returns it when the street has rows in the gmina's street list
   and no row covers the house number (`rows.Count > 0 && schoolIds.Count == 0` in `GetCatchment`).
2. **`no_match` keeps two meanings:** the street is not in the gmina's street list (including a missing `street`
   parameter), and, deliberately, a row covers the number but its school is not public. In the second case the code
   returns `no_match` with no school, because `items` is empty when the school fails `IsPublic`. The code comment in
   `GetCatchment` records this.
3. **`no_data` and `street_list_match` are unchanged.**
4. **Every consumer reads the new value and falls back safely:**
   - the endpoint's `/// <summary>` doc and the `MatchNoNumberMatch` constant describe the new value;
   - the MCP tool description of `get_catchment_school` (`src/SchoolSearch.Mcp/Tools/SchoolTools.cs`) names `no_number_match`, and `AgentToolRunner.MatchSummary` gives it its own Polish summary;
   - the web (`web/app/src/components/catchment-panel.tsx`) shows separate Polish copy for `no_number_match`: "Tego numeru nie ma na liście obwodu tej ulicy. Sprawdź numer albo uchwałę gminy."; `web/app/src/lib/api.ts` lists the value in the `match` type;
   - `docs/mcp/MCP.md` lists the four values and what each means.
5. **Unknown values fall back to the street-not-found message** in the web panel. A client built before this change that
   receives `no_number_match` shows the street-not-found message. That is the pre-change behaviour for this case: no
   school is invented, but the wording stays wrong until that client is updated.
6. **Tests:** contract tests in `tests/SchoolSearch.Api.Tests/Public/MethodologyGminaCatchmentContractTests.cs` cover a
   number outside the street range, a parity mismatch, a street absent from the list (`no_match`), an unknown street on
   the demo gmina with a house number (`no_match`), and a listed street with numbers outside every rule (`no_number_match`).
   **These tests were not compiled or run locally** (no .NET SDK in this environment). CI is the first check.

## Options considered

| Option | For | Against |
|---|---|---|
| **New value `no_number_match`, `no_match` kept for the other cases (chosen)** | The value the client branches on says what happened. The web page and the MCP tool can give the right message without a second field. The change is additive: existing values keep their meaning for every case that already had one. | Clients that switch on `match` must learn a new value. `no_match` still covers two cases (unknown street and non-public school), so it is not a full description on its own. |
| Keep `no_match` for both cases and add a `reason` field (`street_unknown`, `number_not_covered`, `school_not_public`) | One value is unchanged for all clients. The `reason` field carries more detail, including the non-public case. | A client that ignores `reason` still shows the wrong message, so the bug is not fixed for them. Two fields must stay in step across the endpoint, the MCP text, the web and `MCP.md`. The web branches on the value, so it would have to read a second field for the fix. |
| Client-side inference: the client fetches the street list and decides whether the street is listed | No API change. | The matching rule (normalisation, ranges, parity) would be copied into the web and the MCP agent. The API owns that rule, and a copy can drift from it. It also needs the whole street list per gmina, which the endpoint does not return. |

## Consequences

- **Easier:** a parent with a real street and a number outside the ranges gets a message about the number, not about the
  street. The MCP agent and the tool text can explain the difference between "not on the list" and "on the list, number
  not covered".
- **Additive contract change.** No value that existed before has moved to a new meaning, except that the
  "listed street, uncovered number" case now returns `no_number_match` instead of `no_match`. A client that only
  handles `street_list_match`, `no_match` and `no_data` falls back to its own street-not-found message for the new
  value. No school is shown for it, so the fallback is safe.
- **Harder:** every new match value is a contract change. The MCP tool text, `MCP.md`, the web type and the MCP agent
  summary all have to list it. If a fifth value is added later, the same five places need the change, and a missing one
  shows the wrong copy with no compile error (the web type is a TypeScript union, which checks only the web's own code).
- **Must be checked:** CI must pass the contract tests above. They are the first compiled check of this change.
- **Doc gap:** `docs/mcp/MCP.md` says `no_match` means "the street is not on the list". That is incomplete: a covering
  rule whose school is not public also returns `no_match`. This ADR does not change `MCP.md` wording; it is a follow-up
  if the product keeps the current behaviour.

## Open points

1. **Non-public school case.** A rule covers the house number, but its school is not public, so the response is
   `no_match` with no school. Is `no_number_match` the better value here? The argument for it is that the street and the
   number are both found, so `no_match` ("not on the list") is wrong for this case too. The argument against it is that
   `no_number_match` tells the parent the number is the problem, when the real reason is that the school is not in the
   public catalogue. **Decision owner: product.** Until product decides, the behaviour is `no_match`, as built.
2. **Number not supplied.** If `number` is empty and the street has any ranged or parity rule, the response is
   `no_number_match`, and the web shows "Tego numeru nie ma na liście obwodu tej ulicy", although the parent entered no
   number. The copy is wrong for that input. **Decision owner: product** (for the copy, and for whether an empty
   number should instead ask for one).
