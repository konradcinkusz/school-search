# Security review: Phase F (monetisation: premium profiles, parent enquiries, subscriptions)

Scope: the Billing service (`src/SchoolSearch.Billing`), its webhook and simulate endpoints, claims and subscriptions,
the premium profile and its sponsored text, parent enquiries, the web BFF calls to billing, the AI profile-draft
assistant, and the deploy path for billing secrets.

**Status of this review: design review, not a penetration test.** Nothing here has run against a deployed system or
against a real payment provider (none is chosen, ADR-0016). Every control is recorded as **DESIGN** (written in the spec
or the code) or **NOT RUN** (a test or check that has not executed in CI or on a deployment). A gate that could not run
is reported as NOT RUN, not PASS.

Owner of the controls: the file names in the table. Decisions: ADR-0016 (service boundary, provider-neutral webhook,
no paid effect on scores). Earlier reviews: `SECURITY-REVIEW-PHASE-C.md`, `SECURITY-REVIEW-PHASE-E.md`.

## 1. Assets

| Asset | Why it matters |
|---|---|
| Payment provider webhook secret (`Billing__WebhookSecret`) | Whoever holds it can forge a "payment succeeded" event and grant a subscription |
| Subject pepper (`Billing__SubjectPepper`) | Keys the subject hash of claims and subscriptions. Lost or leaked, it allows a dictionary check of known subjects |
| Entitlement (`EntitlementService.IsPremium`) | Decides whether a school shows a sponsored block. Wrong grants are a consumer and a trust problem |
| Parent enquiry content and e-mail address | Personal data of parents, possibly with data of a child in the free text (RODO) |
| Sponsored text (headline, description, link) | Shown on a public page under the Serwis name. Must not look like the Serwis's own judgement |
| The scoring and ranking outputs | Must never change because of a payment. Protected by absence, not by a flag |

## 2. Threat model and controls

| # | Threat | Control | Where | State |
|---|---|---|---|---|
| F1 | A forged webhook grants a subscription | HMAC-SHA256 over the raw body, hex, compared with `CryptographicOperations.FixedTimeEquals`. Length and hex-parse failures count as failures, not exceptions. Outside Production with no secret, mock mode accepts; in Production with no secret, every webhook is rejected (401) | `src/SchoolSearch.Billing/Payments/**` (F2), `Billing/Payments/WebhookSignature` | DESIGN. **NOT RUN** against a real provider |
| F2 | Unsigned webhooks accepted in Production (the mock fallback made symmetric) | Reject in Production when no secret is set. The `MockMode` default is false in Production | F2 tests "unsigned rejected in production" | DESIGN. **NOT RUN** in CI until the test is merged |
| F3 | Replay of a valid event grants twice | Idempotency on the provider event id (`WebhookEvent.EventId` primary key). A redelivery is a recorded no-op | F2 `BillingWebhookHandler` (tests: double grant) | DESIGN. **Residual:** no timestamp tolerance is specified. A replay of a valid event is accepted only as a duplicate. Confirm the provider's timestamp rules before go-live |
| F4 | Simulate endpoint used in Production to grant access | `POST /api/v1/billing/simulate` answers 403 unconditionally when `MockMode` is false, and requires a verified user. It builds a signed event and calls the same handler as the webhook (no bypass) | F2 `MapPaymentEndpoints` | DESIGN. **NOT RUN** on a deployment. Production sets `Billing__MockMode = "false"` explicitly (`flyio/billing.fly.toml`) |
| F5 | Price or amount chosen by the client | The client sends a plan code only. Plans and prices come from server configuration (`Billing:Plans`). Amount and currency are not inputs to entitlement | F1 `PlanCatalog`, F1 checkout endpoint | DESIGN |
| F6 | Entitlement granted by a browser flag or a success redirect | Entitlement is computed on the server from the subscription row. The success redirect is not an input | F1 `EntitlementService` | DESIGN |
| F7 | IDOR on claims, subscriptions and enquiries | Every owner route checks `OwnerHash` against the caller's subject hash. Enquiry reads and deletes check the school owner | F1 `Claims/**`, `Subscriptions/**`; F3 `Leads/**` | DESIGN. Tests to be run. **NOT RUN** |
| F8 | Claim squatting (a stranger claims a school to sell its profile) | A claim is pending until a moderator approves it outside the system. Only an approved claim with an active subscription allows a profile. Moderation actions are audited with the moderator's hashed id | F1 moderation endpoints, `ClaimLinter` | DESIGN. **Residual:** the approval rule depends on the editors' manual check, which is outside the system |
| F9 | Sponsored text makes a false or ranking claim | `ClaimLinter` rejects a list of superlatives, ranking terms, guarantees, percentages and the word "verified" (only the platform may say it). The AI draft is linted and never saved. Plain text only | F1 `ClaimLinter` (used by PUT and the draft) | DESIGN. **Residual:** a linter is a list of terms, not a meaning check. Moderation is the second control |
| F10 | XSS through sponsored text | Plain text only. Rendering in React escapes it. The website link must be `https`, at most 300 characters. The web app renders the link with `rel="sponsored nofollow noopener"` | F1 validation, F4 web component | DESIGN. **NOT RUN** in e2e |
| F11 | Paid promotion hides or lowers a review | Impossible by design: no code path from billing to reviews or to the review list (architecture test) | `tests/SchoolSearch.Architecture.Tests/MonetisationBoundaryTests.cs` | DESIGN. Architecture rule runs in CI |
| F12 | Paid content enters a score, a ranking or an MCP reply | No billing reference from scoring, the catalog, reviews or the score contract types. The score contract types carry no premium, sponsor, paid, billing, entitle or subscription member. Api sort options name no paid order. MCP exposes no billing data (`docs/mcp/MCP.md`) | `MonetisationBoundaryTests.cs`, `docs/mcp/MCP.md` | DESIGN. Architecture rules run in CI |
| F13 | Parent e-mail address or message leaks into logs | No logging of the e-mail address or the message body. Logs carry the event id and the outcome only | F3 | DESIGN. **NOT RUN** (log review on a deployment) |
| F14 | Parent data kept longer than stated | `LeadPurgeService` (hosted) deletes leads past `ExpiresAt` (created plus 90 days). Index on `ExpiresAt` | F3 | DESIGN. **NOT RUN** |
| F15 | Bot spam on the enquiry form | Honeypot field (a filled field gives no write), consent checkbox, rate limit of 5 per hour per IP through the BFF forwarded address, body limit 16 KB | F3, F1 rate limiting | DESIGN. **NOT RUN** |
| F16 | Secret on a command line or in a log | `flyctl secrets import --stage` on stdin in `flyio.yml` (`deploy_billing`). Values never echoed. Only the names of missing secrets are printed | `.github/workflows/flyio.yml`, `flyio/SECRETS.md` section 2 | DESIGN. **NOT RUN** on Fly |
| F17 | Subject pepper reused for reviews, or rotated | A separate variable (`Billing__SubjectPepper`, GitHub `BILLING_SUBJECT_PEPPER`). Its deploy job refuses to run without it. The value must never change once subjects are stored | `flyio/SECRETS.md`, `docs/runbooks/BILLING.md` section 3 | DESIGN |
| F18 | Prompt injection through the AI profile draft | The owner's bullets are data, the system prompt forbids invented facts, superlatives and verification claims. The output is parsed defensively, linted, and returned to the owner only. Never saved or published. The assistant is off without a key (503 `ai_unavailable`) | F6 `src/SchoolSearch.Billing/Ai/**` | DESIGN. Fake-client tests only. **NOT RUN** against the model |
| F19 | Billing secrets or keys in the repository | Placeholders only in `secrets.env.example`. Gitleaks in the hook and CI | `scripts/scan-secrets.sh`, `.gitleaks.toml` | Scanner exists. The CI scan **NOT RUN** on this change |
| F20 | Billing becomes an identity store | Billing validates authservice tokens and holds no user table and no password hashing. The identity rules cover the Billing assembly once it exists | `tests/SchoolSearch.Architecture.Tests/IdentityBoundaryTests.cs` (ADR-0015) | DESIGN. Rules run in CI once the project lands |
| F21 | SSRF through the website link | None. The link is stored and rendered, never fetched by the server | F1 validation | DESIGN |
| F22 | Rate-limit bypass through a forged client address | Forwarded client addresses are honoured only with the BFF shared secret (constant-time match). Without it, the anonymous bucket applies | `SchoolSearch__TrustProxyClientIpHeader`, `BFF_SHARED_SECRET` (`flyio/SECRETS.md`) | DESIGN. **NOT RUN** |

## 3. Residual risks, not solved in this change

- **No real provider has been tested.** The signature header name and the event taxonomy are unconfirmed. Until they are
  confirmed against sandbox traffic, the webhook is a design, not a verified integration (`docs/runbooks/BILLING.md`
  section 4).
- **The manual approval of claims** is the only control that keeps a school's "verified" status true. It is a process,
  not code.
- **Consumer and tax obligations are not reviewed.** See `docs/legal/SALES-TERMS-DRAFT.pl.md`.
- **Single Postgres instance** holds `catalogdb`, `reviewsdb`, `authservicedb` and `billingdb` (ADR-0015, the deviation
  register). A failure or a bad restore affects every service.

## 4. What the review did not run

| Check | State |
|---|---|
| Unit and integration tests of the Billing service | NOT RUN in this change set (no .NET SDK in the authoring environment; CI runs them) |
| Architecture rules for monetisation and identity | NOT RUN until the Billing project and its Dockerfile land |
| Billing image build and smoke test (`ci.yml`, docker-build) | NOT RUN until the Dockerfile exists |
| Billing deploy job (`flyio.yml`) | NOT RUN. It runs only once `src/SchoolSearch.Billing/Dockerfile` and `flyio/billing.fly.toml` both exist |
| Webhook against a real provider sandbox | NOT RUN. No provider is chosen |
| Login and purchase against a real authservice | NOT RUN in CI, as in Phase E |
| Browser purchase and enquiry flows (Playwright) | NOT RUN for the billing flows against a real authservice (documented in the e2e tests) |
