# ADR-0008: Interim `ADMIN_API_KEY` for admin endpoints, with a Phase E exit

- **Status:** Superseded by ADR-0015 (Phase E exit). `ADMIN_API_KEY` is removed from the code.
- **Date:** 2026-10-11

## Context

Some endpoints must not be public: correction intake handling, import triggers and similar operator actions
(Phases B and C). Accounts do not exist until Phase E (ADR-0004), so no role can yet be asserted from a token.

## Decision

Admin endpoints require a header carrying `ADMIN_API_KEY`. The key is a secret (environment or Fly secret, never
in source), compared in constant time, and never logged. The public read surface does not accept it.
When the key is absent, admin endpoints are disabled and `/health` reports the admin integration as `disabled`
(P8), so the zero-credential run still works.

**Exit condition (Phase E):** admin access moves to a role carried in authservice tokens, validated against its
JWKS, and `ADMIN_API_KEY` is removed from configuration, `secrets.env.example` and `flyio/SECRETS.md`.

## Options considered

| Option | For | Against |
|---|---|---|
| Interim static key with an exit | Simple; unblocks Phases B and C | One shared secret grants full admin; must be rotated by hand |
| No admin endpoints until E | No shared secret | Import and correction handling would have no operator path |
| Build a user store now | Real identity | Forbidden by P5 outside authservice; premature |

## Consequences

Anyone with the key can act as admin, so the key is scoped to operator actions only and rotated on any suspected
exposure. Its presence is listed in the deviation register. The row is removed at the Phase E exit.
