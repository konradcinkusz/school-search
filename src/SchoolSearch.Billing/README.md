# SchoolSearch.Billing

Monetisation core (Phase F): the sponsored school profile ("Treść sponsorowana"), school claims, subscriptions and the
entitlement rule. Own deployable and own database (`billingdb`, database-per-service, P3). ADR-0016.

Scoring and search never reference this service (architecture-tested). Paid content is never shown in lists, rankings,
search order or score blocks, and no payment can change a score, a rank, the order of reviews, or hide any data.

## What it owns (F1)

- Claims: a self-declared representative claims a school. Only a moderator's approval (manual check outside the system)
  makes it count ("Zweryfikowane przez redakcję"). One row per owner and school.
- Subscriptions: one row per school. Cancel and resume only flip `CancelAtPeriodEnd`; nothing is deleted.
- Premium profiles: headline, description and https website, plain text, checked by `ClaimLinter`.
- Entitlement (`Subscriptions/EntitlementService.cs`): premium holds only when an approved claim belongs to the
  subscription's owner, the subscription is `active` or `canceling`, `CurrentPeriodEnd` is in the future, and the profile is
  not locked. Expiry locks the profile (`subscription_expired`) and never deletes it.
- Plans from configuration (`Billing:Plans`). Empty by default: the pricing page says "Cennik w przygotowaniu.".

Not here: webhooks and the payment provider (`Payments/`, F2), parent enquiries (`Leads/`, F3), the AI draft assistant
(`Ai/`, F6). Those subfolders are wired in `Program.cs` through the `WIRING-*` markers.

## Endpoints

| Method | Path | Access |
| --- | --- | --- |
| GET | `/api/v1/plans` | anonymous |
| GET | `/api/v1/schools/{slug}/premium` | anonymous, public envelope with the label |
| PUT | `/api/v1/schools/{slug}/premium` | verified e-mail, approved claim, live subscription |
| POST | `/api/v1/schools/{slug}/claims` | verified e-mail; re-opens a rejected claim |
| GET | `/api/v1/claims/mine` | verified e-mail; own claims and subscriptions |
| POST | `/api/v1/subscriptions/checkout` | verified e-mail, approved claim; `503 billing_unavailable` outside mock mode without a provider |
| POST | `/api/v1/subscriptions/{id}/cancel` | authenticated owner |
| POST | `/api/v1/subscriptions/{id}/resume` | authenticated owner |
| GET | `/api/v1/moderation/claims?status=` | Admin or SuperAdmin |
| POST | `/api/v1/moderation/claims/{id}/decision` | Admin or SuperAdmin; `approve`, `reject` or `revoke` |

Every error uses `{"error":{"code","message","traceId"}}`. Auth-gated endpoints answer `503 auth_unavailable` when
`Auth:Authority`, `Auth:Issuer` or `Auth:Audience` is unset (P8).

## Configuration

| Key (env with `__`) | Meaning |
| --- | --- |
| `Auth:Authority`, `Auth:Issuer`, `Auth:Audience` | authservice; `AuthService` defaults are refused |
| `Billing:SubjectPepper` | secret pepper for owner hashes. Required in Production |
| `Billing:MockMode` | default true outside Production, false in Production. Simulate answers 403 unless mock mode is on |
| `Billing:WebhookSecret` | secret for webhook signatures (F2). Health lists `billing-provider` as disabled while unset or in mock mode |
| `Billing:SignatureHeader` | header that carries the webhook signature; default `X-Signature`, to be confirmed |
| `Billing:CheckoutBaseUrl` | base of the synthetic mock checkout URL |
| `Billing:Plans__0__Code`, `__Name`, `__PriceGrossPln`, `__Interval` | plan catalogue; invalid or duplicate codes stop the service at startup |
| `Billing:ExpirationIntervalMinutes` | interval of the expiry pass (default 15) |
| `BFF_SHARED_SECRET` | shared secret with the web BFF; the BFF forwards `X-Client-Ip` only with this secret |
| `DATABASE_PROVIDER`, `ConnectionStrings:billingdb` | `postgres` with a connection string; otherwise InMemory (Development and Staging) |

No price, VAT rate, invoicing rule or provider name is set in code. Prices stay `null` until configured.

## Rules that must hold

- The claim linter (`Claims/ClaimLinter.cs`) rejects superlatives, rankings, guarantees and unverifiable badges in profile
  text (400 `claim_not_allowed`, naming the matched term). Only the platform may say "verified", via moderator approval.
- Profile text is plain text: no angle brackets, no control characters.
- Premium is granted only by the entitlement rule above. Amounts and client flags are never inputs to it.
- Expiry never deletes. A lock carries a reason and a renewal unlocks it.

## Verification

Hand-written Npgsql migration `20261027000000_Billing_Initial` (no model snapshot yet, see the migration folder).
Tests: `tests/SchoolSearch.Billing.Tests` (WebApplicationFactory, InMemory, test auth scheme registered only there).
