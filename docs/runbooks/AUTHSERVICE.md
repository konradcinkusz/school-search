# Runbook: authservice (identity provider)

authservice (`konradcinkusz/authservice`, external and read-only here) is the **only** identity provider and the
**only** holder of a signing key in school-search (ADR-0004, ADR-0015). This repository validates tokens and mints
none. This runbook covers deploy, keys, databases, rotation and the post-deploy assertions.

Facts about authservice come from `AUTHSERVICE-BRIEF` (source commit d748af5, release v0.3.4). Items marked
**UNVERIFIED** must be checked on the first deploy before anyone relies on them (section 7).

**No key material belongs in this file, in a commit, in an issue, in a chat or in a log.** Keys live in the GitHub
environment `dev` and in Fly secrets. This runbook names the places; it never shows a key.

## 0. Preconditions

- The image is pinned: `ghcr.io/konradcinkusz/authservice:v0.3.4` (the latest release at the time of the brief).
  Check the release list before each deploy and pin the newest release, never `:latest`.
- The GHCR package is private by default. **UNVERIFIED** whether the owner has made it public (G6). If it is private,
  Fly needs a pull credential (`flyio/SECRETS.md` section 3). Verify with a first deploy.
- The Fly app `school-search-authservice-dev` is created by the workflow. The database and the key are not.

## 1. Generate the signing key (once)

The key is RSA 2048 (the minimum; authservice requires 2048 bits or more, PKCS#8 PEM, brief section 1).

```bash
# Run on a trusted machine. Write to a file outside the repository, with restrictive permissions.
umask 077
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$HOME/jwt-signing-school-search.pem"
```

Store the value as the GitHub environment `dev` secret `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`, on **one line** with literal
`\n` escapes for newlines. A conversion that does not print the key:

```bash
awk 'BEGIN{ORS="\\n"} {print}' "$HOME/jwt-signing-school-search.pem" > "$HOME/jwt-signing-school-search.oneline"
# Paste the contents of that file into the GitHub secret (Settings > Environments > dev > Secrets), then:
shred -u "$HOME/jwt-signing-school-search.pem" "$HOME/jwt-signing-school-search.oneline"
```

Rules:
- Keep one key per product. Never reuse the key of another project (brief section 4, VERIFIED).
- The file never enters the repository. `.gitignore` and the gitleaks rules do not replace this rule.
- To rotate, see section 6. Do not overwrite the live key in place.

## 2. Create the databases (once)

One Postgres instance (`school-search-postgres`, `--ha=false`) holds three databases, one per service:
`catalogdb` (api, created by the Postgres image), `reviewsdb` and `authservicedb`. CI does not create them.

Connect over Fly's proxy, not over a public listener (the Postgres app has none). The commands below are the usual
`flyctl` shape. **Verify the proxy syntax against your flyctl version before running it.**

```bash
flyctl proxy 15432:5432 --app school-search-postgres &
read -r -s -p "POSTGRES_PASSWORD: " PGPASSWORD; echo; export PGPASSWORD
psql "host=localhost port=15432 user=schoolsearch dbname=catalogdb" \
  -c "CREATE DATABASE reviewsdb;" \
  -c "CREATE DATABASE authservicedb;"
unset PGPASSWORD
kill %1
```

If a database already exists, `CREATE DATABASE` fails with "already exists". That is the expected answer on a rerun.

The workflow then assembles `ConnectionStrings__reviewsdb` and `ConnectionStrings__DefaultConnection` from
`POSTGRES_PASSWORD` and the user `schoolsearch` (`POSTGRES_USER` in `flyio.yml`). If the database is missing, the
deploy health check fails. That is the intended failure point.

## 3. Deploy

Deploys come from a `v*` tag in CI, never from a laptop (AGENTS.md, hard rules).

1. Set the repository variables: `AUTHSERVICE_IMAGE` (the pinned tag) and `AUTHSERVICE_ENABLED=true`.
2. Set the GitHub secret `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` (section 1) and confirm `POSTGRES_PASSWORD` exists.
3. Push a tag. The `deploy_authservice` job:
   - creates the app if missing;
   - imports `ConnectionStrings__DefaultConnection` and `Jwt__PrivateKeyPem` with `flyctl secrets import --stage`, on stdin;
   - deploys `flyio/authservice.fly.toml` with `--image` (the pinned tag, no rebuild);
   - runs `scripts/assert-authservice.sh` (section 4).

The first boot is slow, not failed: schema migration runs in a background service after the listener starts (brief
section 4). `/health/ready` answers 503 until it finishes. The assertion script retries each request (20 attempts, 5 seconds apart),
which may not cover a very slow first migration. If it times out, read the Fly logs before re-running.

Expected first-boot log line (brief section 5): `AuthService starting. Provider=..., SigningAlgorithm=RS256, ...`.
An `HS256` warning means the key is not set. Stop and fix the key before anything else.

## 4. Post-deploy assertions

`scripts/assert-authservice.sh <public-base-url> <expected-issuer>` runs in the workflow and fails the deploy when:

| Check | Pass condition | Why |
|---|---|---|
| `GET /health` | 200, `status` Healthy | liveness |
| `GET /health/ready` | 200, `status` Healthy | schema and database ready |
| `GET /.well-known/openid-configuration` | `issuer` equals the expected issuer; `id_token_signing_alg_values_supported` is exactly `["RS256"]`; `jwks_uri` starts with the public origin | a relative or internal `jwks_uri` means `Jwt__PublicBaseUrl` is missing (IDX10500 before v0.3.4) |
| `GET /.well-known/jwks.json` | at least one key; every key RSA, `RS256`, `sig`, with a `kid` | an empty `keys` array means HS256: every consumer rejects every token. Health cannot see this |
| `GET /api/v1/auth/me` without a token | 401 | authentication is enforced |

The expected issuer is `school-search`, the value of `Jwt__Issuer` in `flyio/authservice.fly.toml` and of
`Auth__Issuer` in the consumers. The two must match.

Optional end-to-end check (manual, never in CI): register a test account, log in, call `/api/v1/auth/me` with the
bearer token, and confirm that `api` accepts the token. Do this only with a throwaway account.

## 5. Local development (opt-in)

The AppHost runs authservice as a container only when asked (`IncludeAuthservice=true`). It needs a local RS256 key,
which is a dev key, not the Fly key:

```bash
dotnet user-secrets set "Authservice:JwtPrivateKeyPem" "<one line with \n escapes>" --project src/SchoolSearch.AppHost
dotnet user-secrets set "IncludeAuthservice" "true" --project src/SchoolSearch.AppHost
dotnet run --project src/SchoolSearch.AppHost
```

authservice then listens on `http://localhost:8090`. Its issuer and audience are `school-search`. The local database
`authservicedb` is created by Aspire in the local Postgres container. Without a key, the AppHost stops with a message
that names the missing secret, because an HS256 authservice would publish an empty JWKS.

HTTPS is not used locally (`Development`). Production-only checks such as `RequireHttpsMetadata` are not exercised
here. **Login and registration against a real authservice are NOT RUN in CI.**

## 6. Rotation

Rotate on a schedule, or at once when a key may have leaked.

1. Generate a new key (section 1). The old key is never written into the repository.
2. Set `Jwt__PreviousPublicKeyPem` (Fly secret, authservice app) to the **old public key** and `Jwt__PrivateKeyPem` to the
   new private key. The old key stays in the JWKS, so tokens signed before the rotation still verify (brief section 1).
3. Keep the previous key for one refresh-token lifetime (30 days when MCP is configured), otherwise for
   `Jwt:ExpirationMinutes` (default 60).
4. Remove the previous key after that window. Record the rotation date in the deviation register or here.

**Leaked key, immediate:** rotate and remove the previous key in the same deploy. Every session signed with the old key
is then invalid. Expect users to sign in again. Record the incident in `docs/security/`.

Never rotate `Reviews__SubjectPepper` with the signing key. They are unrelated (see `flyio/SECRETS.md`).

## 7. Open points to verify on the first deploy

| Ref | Open point | How to check | Consequence if wrong |
|---|---|---|---|
| G1 | The database provider variable: `DatabaseProvider` (README) or `DATABASE_PROVIDER` (DEPLOYMENT.md) | Startup log names the provider | Silent fallback to a wrong provider. Check the log line |
| G3 | HTTPS redirect loop behind Fly's proxy (`UseHttpsRedirection`, no trusted proxies by default) | `curl -sI https://<app>.fly.dev/.well-known/openid-configuration` returns 200, not 301/308 | Every discovery request redirects; the assertion fails. Configure trusted proxies (`Network__TrustAllProxies` or `KnownProxies`) and re-check. Do not guess the setting blind |
| G4 | Wire names of role and name claims (`role` or the long `http://schemas.microsoft.com/...` form) | Decode one real access token (payload is base64url JSON, not verified; do not log it) | Role checks fail. The consumers accept both forms; if neither appears, stop and fix the mapping |
| G5 | 2FA completion and external-login exchange response shapes | Read `TwoFactorController` and `ExternalAuthController` in the authservice repository | The web BFF cannot complete 2FA. The UI states it is not supported yet |
| G6 | Whether the GHCR package is public | GHCR package settings | First deploy fails at pull |
| G7 | Refresh-token rotation makes concurrent refreshes destructive | Test: two refreshes with the same token in one session | The BFF logs the user out. The BFF uses a single-flight refresh per session (web) |
| G10 | The user id is a string GUID | Compare `sub` of a real token with `ApplicationUser` | Reviews author hash uses `sub` as text, so a format change would not break it, but the id join would |

Record each result in `docs/security/SECURITY-REVIEW-PHASE-E.md` with the date and the result, and remove the open
point from this table only when it is verified.
