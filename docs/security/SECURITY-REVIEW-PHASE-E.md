# Security review: Phase E (accounts and reviews)

Scope: authservice integration, token validation in the Api and the Reviews service, the web BFF session, the
review and notice flows, moderation, personal data in reviews, and the deploy path for secrets.

**Status of this review: design review, not a penetration test.** Nothing here has been run against a deployed
system. Every control is recorded as **DESIGN** (written in the code or the spec) or **NOT RUN** (a test or check that
has not executed in CI or on a deployment). Gates that could not run are reported as NOT RUN, not as PASS.

Owner of the controls: the file names in the table. Decisions: ADR-0004 (no users before Phase E), ADR-0015
(authservice and reviews, written by the Phase E change set). Earlier review: `SECURITY-REVIEW-PHASE-C.md`.

## 1. Assets

| Asset | Why it matters |
|---|---|
| Access tokens (issued by authservice) | Grant an account, and the moderator role for admins |
| Refresh tokens (held by the BFF, server-side) | Long-lived; rotation revokes the family on reuse |
| The authservice signing key | Lets anyone mint a token accepted by every service. The only key in the estate |
| `Reviews__SubjectPepper` | Keys the author hash. Lost or leaked, it allows a dictionary check of known subjects against stored hashes |
| `SESSION_SECRET` | Decrypts and forges the BFF session cookie |
| Review text | User-generated; may contain names or sensitive data about children and staff |
| Moderator decisions and statements of reasons | The record that the DSA process relies on |

## 2. Threat model and controls

| # | Threat | Control | Where | State |
|---|---|---|---|---|
| T1 | Forged token accepted by a service | RS256 only, JWKS from the authservice discovery document, issuer and audience checked, `ClockSkew = 0`, `RequireHttpsMetadata` true outside Development, `MapInboundClaims = false` | `src/SchoolSearch.Api/Security/**`, `src/SchoolSearch.Reviews/Auth/**` | DESIGN. **NOT RUN** against a real authservice |
| T2 | Algorithm confusion (HS256 with a public key) | Algorithm pinned to RS256 in validation. An HS256 authservice publishes an empty JWKS and fails the deploy assertion | `scripts/assert-authservice.sh`, `flyio.yml` | DESIGN; assertion **NOT RUN** |
| T3 | A token from another product accepted | Distinct issuer and audience per product; the defaults `AuthService` are never used. Challenge tokens (`:2fa`) and MCP tokens carry other audiences | `flyio/*.fly.toml`, `Auth__Audience` | DESIGN |
| T4 | Signing key leaked | One key, in the GitHub environment and Fly secrets only; never in the repository; rotation with `Jwt__PreviousPublicKeyPem`; immediate-rotation procedure | `docs/runbooks/AUTHSERVICE.md` sections 1 and 6 | DESIGN |
| T5 | Secret on a command line or in a log | `flyctl secrets import --stage` on stdin; no `flyctl secrets set KEY=value` in any workflow; no value echoed | `flyio.yml`, `flyio/SECRETS.md` section 2 | DESIGN. The previous workflow passed secrets on the command line; fixed in this change. **NOT RUN** on Fly |
| T6 | Secret value in the repository | Placeholders only in `secrets.env.example`; gitleaks in hook and CI | `scripts/scan-secrets.sh`, `.gitleaks.toml` | Scanner exists. The CI scan **NOT RUN** on this change |
| T7 | Admin role granted by a shared static key | `ADMIN_API_KEY` removed. Admin access requires a validated token with role `Admin` or `SuperAdmin` | ADR-0015, `src/SchoolSearch.Api/Security/**` (E2) | DESIGN; removal depends on the E2 change |
| T8 | Session cookie read or forged by a script | `httpOnly`, `Secure` outside development, `SameSite=Lax`, AES-256-GCM with a random IV, tamper detection | `web/app/src/lib/session.ts` (E3) | DESIGN. **NOT RUN** |
| T9 | Access token leaked to the browser | Tokens stay server-side; the browser gets only `{authenticated, emailVerified}` | `web/app` route handlers (E3) | DESIGN |
| T10 | Cross-site request on a state-changing BFF route | `POST` only, `Origin` must match `Host`, JSON content type | `web/app/src/app/api/**` (E3), the existing proxy pattern | DESIGN |
| T11 | Refresh-token reuse after concurrent refreshes logs the user out (authservice G7) | Single-flight refresh per session; rotated token stored on every refresh | `web/app/src/lib/session.ts` (E3) | DESIGN. Multi-instance limitation documented |
| T12 | Account enumeration through login or registration | Uniform error bodies; authservice's own rate limits (20 per minute per IP on `auth`) | authservice (external). The BFF passes responses through | Relies on authservice. **NOT RUN** |
| T13 | Review spam and abuse | Pre-moderation (`pending` until a moderator publishes); 10 writes per hour per subject; one review per author, school and year (unique index) | `src/SchoolSearch.Reviews/**` (E1a) | DESIGN |
| T14 | Notice flooding (anonymous) | 10 notices per hour per IP; 16 KB body limit | Reviews service (E1a/E1b) | DESIGN |
| T15 | Personal data in review text (children, staff) | Form warns; moderation removes it; no reviewer identity published; no email stored | Reviews service and `/zglos-tresc` (E3) | DESIGN. Moderation effectiveness is an operational control |
| T16 | Reviewer identified from the database | Author stored as `SHA-256(pepper + sub)` (64 hex chars). Without the pepper the hash cannot be joined to authservice. The pepper is a Fly secret, never printed | `src/SchoolSearch.Reviews/**` (E1a) | DESIGN. Weakness: the pepper holder can test known subjects. Accepted, recorded in the deviation register |
| T17 | Moderator abuse or an automated decision | Every decision is a human action (`Automated = false`). AI triage only sets an advisory label; it cannot change status | `src/SchoolSearch.Reviews/Dsa/**`, `Ai/**` (E1b, E6) | DESIGN; the architecture test that enforces it is **NOT RUN** |
| T18 | Paid promotion hides negative reviews (UOKiK) | Promotion has no path to visibility, ordering or status of reviews (Phase F, arch test) | Terms section 7 | DESIGN |
| T19 | Reviews distort the school score | Reviews never reach the scoring engine; the architecture test forbids the dependency | `tests/SchoolSearch.Architecture.Tests/IdentityBoundaryTests.cs` (E2) | DESIGN; **NOT RUN** |
| T20 | Rate-limit bypass by a forged client address | The forwarded address is honoured only with `X-Bff-Secret` matching `BFF_SHARED_SECRET` (constant-time) | `ClientIpResolver` (Api and Reviews) | DESIGN. Without the secret, the anonymous bucket applies |
| T21 | Open redirect or SSRF through `REVIEWS_BASE_URL` or `AUTH_BASE_URL` | Both are server configuration read at request time; the BFF allowlists exact paths | `web/app` (E3) | DESIGN |
| T22 | Oversized bodies | 16 KB limit at the BFF and at the services | BFF route handlers, Reviews service | DESIGN |
| T23 | Security headers | Same policy as the Api (`nosniff`, `Referrer-Policy: no-referrer`, CSP, `Cache-Control: no-store` on account and moderation responses) | Reviews service | DESIGN |
| T24 | Unpinned external image | `ghcr.io/konradcinkusz/authservice` pinned to `v0.3.4` in the workflow and the AppHost. Never `:latest` | `flyio/authservice.fly.toml`, `flyio.yml`, `AppHost` | DESIGN |
| T25 | Moderator endpoints reachable by a user | `Moderator` policy (role `Admin` or `SuperAdmin`), checked by the services. The BFF's role check is a convenience, not authorisation | Reviews service (E1a/E1b), BFF (E3) | DESIGN; 403 tests **NOT RUN** |

## 3. Known gaps (open)

| # | Gap | Risk | Exit condition |
|---|---|---|---|
| G-A | Claim wire names (role and name) not verified against a real token (brief G4) | Role checks could fail, or in a worst case match the wrong claim | Decode one real token on the first deploy; the services accept both the short and the long role claim type |
| G-B | No 2FA completion in the web app (brief G5) | Users with 2FA cannot finish login | The UI says so. Close when the response shape is read and tested |
| G-C | Redirect loop risk behind Fly's proxy (brief G3) | Authservice not reachable on its public URL | First deploy: check the discovery URL does not redirect. Configure trusted proxies |
| G-D | Single-instance assumption for the BFF refresh lock | Two instances race on refresh and log the user out | Shared store before a second web instance |
| G-E | Pepper compromise allows dictionary checks of subjects | Linking authors to known subjects, by someone with the pepper and the database | Accepted. The pepper is a Fly secret held by CI and the app; rotate only with a migration plan |
| G-F | No e-mail confirmation of notices, no out-of-court channel (DSA art. 21) | Process completeness, not confidentiality | See `docs/legal/DSA.md` |
| G-G | Login and registration against a real authservice NOT RUN in CI | The integration could fail in production first | Run the manual end-to-end check (runbook section 4) on the first deploy with a throwaway account |

## 4. Verification record

| Gate | State |
|---|---|
| `dotnet build -warnaserror` and `dotnet test` on the Phase E change | NOT RUN in the authoring environment (no .NET SDK). CI is the first compiler |
| Architecture tests (identity boundary, scoring independence, contract members) | NOT RUN |
| Token validation against a real authservice (issuer, audience, RS256, claims) | NOT RUN |
| Post-deploy assertions (`scripts/assert-authservice.sh`) | NOT RUN. Runs in `flyio.yml` after the first authservice deploy |
| `actionlint` on `ci.yml` and `flyio.yml` | NOT RUN. Authoring environment has no actionlint. CI job `actionlint` is the gate |
| gitleaks on the change | NOT RUN in the authoring environment |
| Playwright e2e for the review section and the DSA pages | NOT RUN in the authoring environment; CI job `e2e` |

## 5. Actions for the owner

1. Verify G-A, G-C and G-G on the first authservice deploy. Record the result here.
2. Create the Phase E databases and the GitHub secrets listed in `flyio/SECRETS.md` section 3 before the first tag.
3. Confirm the Fly secret `ADMIN_API_KEY` is absent: `flyctl secrets list --app school-search-api-dev`.
4. Set `Dsa__ContactEmail` before publishing the notice form.
