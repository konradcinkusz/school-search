# ADR-0015: authservice as the only identity provider, token validation in the Api, and a separate Reviews service

- **Status:** Accepted
- **Date:** 2026-10-11
- **Supersedes:** ADR-0008 (interim `ADMIN_API_KEY`). Completes the exit condition of ADR-0004.

## Context

Phase E adds accounts and parent reviews. ADR-0004 keeps identity out of Phases A to D, and ADR-0008 protects the admin
surface with one shared static key until identity exists. Identity is the external service
`konradcinkusz/authservice`, which is read-only from this repository (P5). It issues RS256 access tokens and publishes
its public keys at `/.well-known/jwks.json`. Its wire claim names are not yet verified (gaps G4 and G5 of the authservice integration brief).

Reviews are user-generated content. They must never feed a school score (METRIC-ETHICS), and they must not be joined
back to a person through this database.

## Decision

1. **authservice is the only identity provider and the only holder of a signing key.** This solution has no user table,
   no password handling, no Microsoft.AspNetCore.Identity and no token creation. Source assemblies validate tokens only.
   `tests/SchoolSearch.Architecture.Tests/IdentityBoundaryTests.cs` enforces this.
2. **The catalog Api validates authservice access tokens** (JwtBearer, RS256, keys from discovery and JWKS, `Auth:Issuer`
   and `Auth:Audience` pinned, zero clock skew, `MapInboundClaims = false`). The role is read from `role` or the .NET
   long claim type. The admin group requires an `Admin` or `SuperAdmin` role and a `sub`. `ADMIN_API_KEY` is removed.
3. **Without `Auth:Authority` the service still starts (P8).** The admin group answers 404, and `/health` reports the
   integration `authservice` as `disabled`. Missing `Auth:Issuer` or `Auth:Audience` with an authority fails startup.
4. **Reviews are a separate deployable** (`SchoolSearch.Reviews`, its own database `reviewsdb`, database-per-service).
   It validates the same kind of token, stores only a keyed hash of the subject (SHA-256 over a server-side pepper), and
   never stores e-mail addresses. Its responses are always marked `userGenerated: true`, and it is not referenced by the
   scoring code. The architecture tests enforce the second point.
5. **Moderation is human.** Moderator decisions are the only code path that changes a review's status. Automated
   triage (if any) is advisory and never sets `Automated = true`.

## Options considered

| Option | For | Against |
|---|---|---|
| authservice as the only IdP, tokens validated in each service | One identity and one signing key; no user store here (P5) | Depends on authservice availability; claim names still to verify |
| Build a user store in this repository | No external dependency | Forbidden by P5; a second identity to secure and rotate |
| Keep a shared static admin key | Simple | One secret grants full admin; no per-person audit trail |
| Put reviews inside the catalog Api | Fewer deployables | Reviews would share a database and code path with scoring; harder to keep reviews out of scores |
| One shared auth library for Api and Reviews | No duplicated code | The kernel stays at or under 800 lines; a third shared project for about 80 lines is not worth it |

## Consequences

- Admin access depends on the authservice JWKS. An outage returns 401 for admin calls (fail closed). It does not affect
  the public read surface.
- Tokens must carry the role under a name that is not yet verified. The mapping accepts both the wire name and the .NET
  long name. The first real token decoded at deploy time confirms it (brief G4). Until then, the claim mapping is
  UNVERIFIED.
- The validation code is duplicated in the Api and in Reviews. This is accepted: each service states its own rules in one
  file, and the duplication is small.
- A test-only auth scheme stands in for JWKS validation in the Api tests. Real signature validation is exercised only in
  deployed environments (see the Phase E security review).
- The architecture rule that bans `SecurityTokenDescriptor` is a namespace-prefix match. A reflection check covers
  signatures. Body-level use outside those two checks would be missed; treat new token-related code as a review item.

## Deviations (recorded in the deviation register)

- The Architecture test project references `SchoolSearch.Reviews` only when its project file exists, so the solution builds
  before that project lands. Once it exists, the reference is always present.
- Reviews rules that name `SchoolSearch.Reviews` are string checks. They pass vacuously until that assembly is loaded.
