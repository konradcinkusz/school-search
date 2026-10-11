# ADR-0004: No user accounts until Phase E

- **Status:** Accepted
- **Date:** 2026-10-11

## Context

The product brief places accounts and reviews in Phase E. Identity, when it arrives, is the external service
`konradcinkusz/authservice`, which is never modified here. The constitution (P5) forbids a user store or token
minting in any service that is not the identity service.

## Decision

Phases A to D have no users, no login UI and no JWT validation wiring. `AddJwtAuthentication` may exist in the
kernel but is not called. The web app has no login UI and no session routes. There is no user table.

The only credential in Phases A to D is the interim admin key recorded in ADR-0008.

## Options considered

| Option | For | Against |
|---|---|---|
| No users until E | Nothing to secure before there is anything to protect; avoids a premature user store | Admin surface needs an interim key (ADR-0008) |
| Build login now | Admin and reviews later need it | A login for a system with no accounts is scaffolding nobody asked for; risks a parallel identity |

## Consequences

Phase E wires authservice: JWKS validation in the API, an admin role from its tokens, and a non-empty JWKS
assertion after deploy (constitution P5 and the identity guide). Until then, public search endpoints are
read-only and anonymous. Rate limiting is by IP bucket.
