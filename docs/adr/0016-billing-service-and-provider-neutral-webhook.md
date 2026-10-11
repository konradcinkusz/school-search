# ADR-0016: Billing as its own service, a provider-neutral webhook, and no paid effect on scores

- **Status:** Accepted for the service boundary and the scoring rule. **Pending** for the payment provider and its cost model (decision not yet taken; see Consequences).
- **Date:** 2026-10-11
- **Deciders:** owner (Konrad Cinkusz), to confirm at the Phase F review. The Phase F brief states the rules recorded here.

## Context

Phase F sells one thing: a clearly labelled, owner-written sponsored profile block on a school page (**Profil rozszerzony**), and a parent enquiry form that delivers a message to the school owner's dashboard. Every other product question is out of scope:

- A paid position, order, score, rank or label is never sold, and no paid content enters a list, a ranking, a search order or an MCP score block. `docs/MASTER-PROMPT.md` makes this a Phase F gate: scoring must not reference billing.
- A payment provider is a credential-bearing external dependency with an availability risk (its webhook secret, its API key). Principle P10 says such a dependency becomes a service, and P3 says one service owns its database.
- `docs/guides/PAYMENTS-AND-MONETIZATION.md` (architecture-standards, read-only) fixes the shape of the flow: the provider owns checkout, card data and tax; the signed webhook is the only trusted input; the webhook handler is the single place where money becomes entitlement; the design must not depend on a vendor (P11).
- The guide recommends a merchant of record "as a small team selling globally", but the choice between a merchant of record and a payment gateway is a cost and tax decision. No cost figures and no provider are recorded in the sources this repository can read. Those numbers are NOT FOUND.
- No source in the estate states UOKiK, consumer withdrawal, price display or Polish or EU VAT invoicing rules. `AGENTS.md` forbids inventing a legal basis, so those points stay OPEN in `docs/legal/SALES-TERMS-DRAFT.pl.md`.

## Decision

1. **Billing is a new deployable, `src/SchoolSearch.Billing`, that owns `billingdb`** (database per service, P3). It is the only service that holds the payment webhook secret and the subject pepper for claims and subscriptions. The catalog Api, the Reviews service and the scoring code never reference it. `tests/SchoolSearch.Architecture.Tests/MonetisationBoundaryTests.cs` enforces this.
2. **The webhook is provider-neutral.** The provider's event names and payloads are mapped at the boundary (`IBillingEventMapper`) into one internal `BillingEvent` shape. Nothing downstream of the handler knows the vendor (P11). The only shipped mapper is `GenericJsonEventMapper`, a documented placeholder to be replaced by a provider adapter. The handler is the single code path for the real webhook and for the mock simulate endpoint.
3. **No paid effect on any score.** Entitlement is a single function (`EntitlementService.IsPremium`). Amount and currency are never inputs to it. Plans come from configuration on the server. Paid content is always labelled "Treść sponsorowana" and is never part of a list, ranking, search order, or MCP reply.
4. **Mock-first.** Outside Production, `Billing__MockMode` defaults to true: checkout returns a synthetic URL and `POST /api/v1/billing/simulate` builds a signed event and calls the real handler. In Production the default is false, and simulate answers 403 unconditionally.
5. **Verifiable truth only.** A profile is "Zweryfikowane przez redakcję" only after a moderator approves the claim in the moderation UI. Until then it is not published.
6. **The provider decision is not taken here.** Merchant of record or gateway, the provider's name, its fees and its signature header are **Pending**. The code does not name a provider, and the deployed service runs without one until the owner decides (`docs/runbooks/BILLING.md` section 1).

## Options considered

| Option | For | Against |
|---|---|---|
| **New service `SchoolSearch.Billing` with `billingdb` (chosen)** | Holds a credential and a tax-relevant ledger apart from scoring. Architecture tests can prove scoring has no path to it. Matches P3 and P10. | One more deployable and one more database on the shared Postgres instance (ADR-0015 accepted the same trade for authservice and Reviews). |
| Billing module inside the catalog Api | One deployable, one database | The scoring code and the paid code would share a process and a database, so the scoring rule could only be a convention. The Api already holds `ANTHROPIC_API_KEY` (a recorded P10 deviation). |
| Billing module inside the Reviews service | Reviews already has accounts and moderation | Reviews must stay free of money (ADR-0015). Mixing them would blur the rule that paid promotion never suppresses a review. |
| Merchant of record (default in the guide) | The provider owns most tax and compliance | Higher fee, less checkout control. Not yet costed for this product. |
| Payment gateway | Lower fee, more control | We would own EU VAT, chargebacks and PCI scope. Choosing it is a decision to build a tax function. Not chosen. |

## Consequences

- Scoring, the catalog and reviews are provably free of billing (architecture rules, not only convention). Their APIs carry no paid member: `MonetisationBoundaryTests` checks the score contract types and the Api sort options.
- Adding a provider means writing one adapter behind `IBillingEventMapper`. Changing the provider does not touch entitlement, the lifecycle handlers or the database.
- The deployed service can run before any provider exists: checkout answers 503 `billing_unavailable`, webhooks are rejected in Production, and the premium envelope answers "no profile". This is P8 degradation, not a fault.
- **Pending, must be decided before any real sale:** provider and cost (merchant of record or gateway, recorded with numbers in `docs/`, citing P14); the signature header name and the event taxonomy, confirmed against real traffic; the consumer-law and VAT texts (`docs/legal/SALES-TERMS-DRAFT.pl.md`, OPEN items); the lawyer's approval of the enquiry privacy notice.
- Outbound idempotency keys for provider API calls and refund handling are NOT FOUND in the guide. They are not designed here.
- Exit condition for the provider-neutral placeholder: a provider adapter is merged, passes the webhook tests (double grant, early renewal, cancel only sets the flag, unknown events logged, webhook before order, unsigned rejected in Production, simulate 403 in Production), and the sandbox traffic confirms the header name.

## Deviations (recorded in the deviation register, `docs/architecture/00-ARCHITECTURE.md`)

- The Architecture test project references `SchoolSearch.Billing` only when its project file exists. Until then the billing rules pass vacuously, as the Reviews rules did (ADR-0015).
- The profile-copy assistant (`src/SchoolSearch.Billing/Ai`) copies the request shape of the Api's Anthropic client rather than sharing it. It is the same kind of accepted duplication as the Reviews token validation.
