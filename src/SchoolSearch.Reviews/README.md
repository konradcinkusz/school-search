# SchoolSearch.Reviews

Accounts-backed parent reviews, the right of reply and the DSA notice-and-action machinery. Own deployable and own
database (`reviewsdb`, database-per-service). Phase E, ADR-0015.

## What it owns

- Reviews, replies, notices (DSA art. 16), moderation decisions (statements of reasons, art. 17), complaints (art. 20).
- No user table, no password handling, no token creation. Identity is authservice (the only IdP and signing-key holder).
  This service only validates RS256 access tokens through authservice discovery and JWKS.

## Endpoints (Phase E1a)

| Method | Path | Access |
| --- | --- | --- |
| GET | `/api/v1/schools/{slug}/reviews?page=&pageSize=` | anonymous, published only |
| POST | `/api/v1/schools/{slug}/reviews` | verified e-mail, current terms version |
| GET | `/api/v1/reviews/mine` | authenticated, own reviews, status, reply and statements of reasons |
| DELETE | `/api/v1/reviews/{id}` | authenticated, author only (status becomes `removed`) |
| POST | `/api/v1/reviews/{id}/reply` | verified e-mail, one pending reply per published review |

Every error uses `{"error":{"code","message","traceId"}}`. Auth-gated endpoints answer `503 auth_unavailable` when
`Auth:Authority`, `Auth:Issuer` or `Auth:Audience` is unset (P8). `/health` lists the integration `authservice`.

## Configuration

| Key (env with `__`) | Meaning |
| --- | --- |
| `Auth:Authority` | authservice base URL; metadata at `{Authority}/.well-known/openid-configuration` |
| `Auth:Issuer`, `Auth:Audience` | product-specific values; `AuthService` (the authservice default) is refused |
| `Reviews:SubjectPepper` | secret pepper for author hashes (SHA-256 of pepper + sub). Required in Production |
| `Reviews:TermsVersion` | terms version a review must accept (draft until reviewed by a lawyer) |
| `Reviews:TrustProxyClientIpHeader` | trust `Fly-Client-IP` (deployment sets it behind Fly's proxy) |
| `BFF_SHARED_SECRET` | shared secret with the web BFF; the BFF forwards `X-Client-Ip` only with this secret |
| `DATABASE_PROVIDER`, `ConnectionStrings:reviewsdb` | `postgres` with a connection string; otherwise InMemory (Development and Staging) |

Author identifiers are stored only as hashes, and no e-mail address is stored. Reviews are never aggregated into a
school score.

## Local development seed

`POST /dev/seed` writes the synthetic demo data (reviews on `demo-sp-1` to `demo-sp-3`, a pending review, a review removed
by a moderation decision with its statement of reasons, DSA notices, a complaint, an owner reply and triage labels). The
route exists only when `DevSeed:Enabled` is `true`, `DevSeed:Key` is set and the environment is not Production; otherwise it
answers 404. The header `X-Dev-Seed-Key` must match the key (401 otherwise). A rerun writes only the rows that are missing,
so a seed that failed part-way is completed by the next call. The demo acknowledgement tokens are fixed and guessable: never
enable the route outside local development. The full procedure is in [`docs/runbooks/LOCAL-DEV.md`](../../docs/runbooks/LOCAL-DEV.md)
and the endpoint in [`docs/runbooks/BILLING.md`](../../docs/runbooks/BILLING.md) is for Billing only.

## Verification

Hand-written Npgsql migration `20261020000000_Reviews_Initial` (no model snapshot yet, see the migration folder).
Tests: `tests/SchoolSearch.Reviews.Tests` (WebApplicationFactory, InMemory, test auth scheme registered only there).
