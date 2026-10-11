# Security review: Phase C (public search surface)

Status: **OPEN**. Findings are time-windowed in section 4. Reviewed against the repository working tree on
2026-10-11, after the Phase C code of the read API, security plumbing, corrections, catchment and the web BFF
had been written. Method: `architecture-standards/docs/guides/SECURITY-REVIEW.md` (sections 1 to 10), applied
literally. Evidence is `repo-relative/path:line`.

**What this is not.** This is a static review plus a test suite. It was not compiled or run here (dotnet is not
installed in the review environment), and it does not replace a penetration test. Run the tests in CI before
trusting any PASS below.

## 0. Scope

In scope: `src/SchoolSearch.Api/**` (Search, Catchment, Corrections, Security, RateLimiting, Errors, Demo),
`src/SchoolSearch.Contracts/**`, `src/SchoolSearch.Catalog.Data/**`, `src/SchoolSearch.ServiceDefaults/**`,
`src/SchoolSearch.Import/**` (CLI surface), `web/app/src/app/api/proxy/**` (BFF), `web/app/src/components/**`,
`web/app/next.config.ts`, `flyio/api.fly.toml`, `secrets.env.example`, `Directory.Build.props`,
`.github/dependabot.yml`.

Not in scope: the rest of `web/`, the MCP service (Phase D), the authservice (Phase E, not built).

Docs read for this review: `architecture-standards` `docs/guides/SECURITY-REVIEW.md`,
`docs/guides/TESTING-STRATEGY.md`, `docs/guides/METRIC-ETHICS.md`; the Phase C spec.

## 1. Method notes

- Categories follow SECURITY-REVIEW §4 to §8 plus the OWASP categories that apply to a JSON, read-mostly API.
- "N/A" is used only with the structural reason and the grep that proves it.
- Each finding has the §2 fields. A finding without an attack scenario is a style note and is listed as such.
- Severity: Critical, High, Medium, Low. Priority: P1 now, P2 within a week, P3 within two weeks, P4 long-term.

Proof greps that support N/A results:

- No raw SQL: `grep -rn "FromSql\|ExecuteSql\|SqlQuery" src` returns nothing. The name filter is a parameterised
  `EF.Functions.ILike` with escaped wildcards (`src/SchoolSearch.Api/Search/SchoolReadModel.cs:155-160`, `:190`).
- No file upload on HTTP: no `IFormFile` or `ReadFormAsync` in `src/SchoolSearch.Api`. The Api project does not
  reference `SchoolSearch.Import` (`src/SchoolSearch.Api/SchoolSearch.Api.csproj`).
- No markdown rendering in the web app: no markdown or rehype package in `web/app/package.json`.
- No admin UI in the web app: `find web/app -iname "*admin*"` returns nothing.
- No passwords or accounts: ADR-0004 (no users until Phase E). Password hashing and email confirmation are N/A.

## 2. Endpoint by role matrix (§8)

Roles: **anon** (no header), **key** (valid `X-Api-Key`), **admin ok** (`X-Admin-Key` equal to `ADMIN_API_KEY`),
**admin bad** (wrong or missing header, key configured), **admin unset** (`ADMIN_API_KEY` not configured).

| Endpoint | anon | key | admin ok | admin bad | admin unset | Source |
|---|---|---|---|---|---|---|
| GET /api/v1/schools, /schools/{slug}, /ranking, /compare, /methodology, /gminas, /catchment, /meta | 200 | 200, key bucket | n/a | n/a | n/a | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:40-47`, `src/SchoolSearch.Api/Catchment/CatchmentEndpoints.cs:36`, `src/SchoolSearch.Api/Endpoints/CatalogEndpoints.cs:19` |
| POST /api/v1/keys | 201, IP limit 5/h | 201 | n/a | n/a | n/a | `src/SchoolSearch.Api/Security/ApiKeyEndpoints.cs:18-20` |
| POST /api/v1/corrections | 201, flood guard | 201 | n/a | n/a | n/a | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:34` |
| GET /api/v1/corrections/{token} | 200 or uniform 404 | same | n/a | n/a | n/a | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:35` |
| GET /api/v1/admin/corrections | 401 | 401 | 200 | 401 | 404 | `src/SchoolSearch.Api/Security/AdminEndpoints.cs:40-42`, `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:38-50` |
| PATCH /api/v1/admin/corrections/{id:guid} | 401 | 401 | 200 or 409 or 404 | 401 | 404 | `src/SchoolSearch.Api/Security/AdminEndpoints.cs:43` |
| POST /api/v1/admin/keys/{id:guid}/revoke | 401 | 401 | 200 or 404 | 401 | 404 | `src/SchoolSearch.Api/Security/AdminEndpoints.cs:44` |
| GET /health, /alive | 200 (exempt from limits) | same | n/a | n/a | n/a | `src/SchoolSearch.ServiceDefaults/ServiceDefaultsExtensions.cs:57-65` |
| GET /openapi/v1.json | 200 (see F6) | 200 | n/a | n/a | n/a | `src/SchoolSearch.ServiceDefaults/ServiceDefaultsExtensions.cs:55` |

Deny by default: there is no global authorization. Public routes are an explicit list. The only privileged group
(`/api/v1/admin`) carries an endpoint filter on the whole group (`src/SchoolSearch.Api/Security/AdminEndpoints.cs:40`). The BFF forwards
only allowlisted routes and never admin paths (`web/app/src/app/api/proxy/[...path]/route.ts:24-36`). **PASS.**

## 3. Checklist results

Result key: PASS, FAIL (finding listed in section 4), N/A (justified above or inline), NOT VERIFIED (cannot be
checked here).

### 3.1 Address and query input (search, catchment, ranking)

| # | Item | Result | Evidence |
|---|---|---|---|
| S1 | `q` requires two or more characters; matched as a parameterised ILike with escaped wildcards | PASS | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:26`, `:79`; `src/SchoolSearch.Api/Search/SchoolReadModel.cs:159-160`, `:190` |
| S2 | No explicit maximum length on `q`; bounded by the Kestrel request-line limit (default 8 KB) | PASS, note | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:78` |
| S3 | Latitude, longitude and radius are parsed invariant-culture, bounded and clamped (radius 0.1 to 50 km) | PASS | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:30-31`, `:367-377`, `:380-393` |
| S4 | `page` and `pageSize` clamped (page at least 1, pageSize 1 to 100) | PASS | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:395-399` |
| S5 | `sort` accepts only `score` (descending) or the default; no direction parameter exists | PASS | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:91`; `src/SchoolSearch.Contracts/Public/RankingPage.cs:6-9`; test `tests/SchoolSearch.Api.Tests/Security/OpenApiSortParameterTests.cs` |
| S6 | Gmina and TERYT validated as exactly seven digits | PASS | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:407`; `src/SchoolSearch.Api/Catchment/CatchmentEndpoints.cs` (`IsTeryt`) |
| S7 | Catchment street length, normalisation and house-number parsing bounded | PASS | `src/SchoolSearch.Api/Catchment/CatchmentEndpoints.cs:37-45`, `TryParseHouseNumber` |
| S8 | Corrections: slug length, field allowlist, message bounds, email shape | PASS | `src/SchoolSearch.Api/Corrections/CorrectionRequestValidator.cs:20-27`, `:39`, `:53` |
| S9 | Email shape regex is linear and carries a timeout | PASS | `src/SchoolSearch.Api/Corrections/CorrectionRequestValidator.cs:39-41` |
| S10 | Malformed JSON and oversize bodies return the uniform error, not a 500 | PASS | `src/SchoolSearch.Api/Security/JsonBodyReader.cs:21-35` |

### 3.2 Imported-file path handling (Import CLI, `--file`)

The Import CLI is operator-run. It is not reachable over HTTP. The rows below cover its input handling.

| # | Item | Result | Evidence |
|---|---|---|---|
| I1 | `--file` path used as given; no allowlisted directory or traversal check | PASS as operator tool, F10 | `src/SchoolSearch.Import/Program.cs:101`, `:114`, `:126` (`File.OpenRead(file)`) |
| I2 | Column-map JSON rejects unknown members (a misspelt override cannot fall back silently) | PASS | `src/SchoolSearch.Import/Common/ColumnMapLoader.cs:16` |
| I3 | Person-data headers refused before any row is read (whole file rejected) | PASS | `src/SchoolSearch.Import/Common/PersonalDataGuard.cs:21-36`; `src/SchoolSearch.Import/Sio/SioLoader.cs:336`; `src/SchoolSearch.Import/Cke/CkeLoader.cs:55-56`; `src/SchoolSearch.Import/Cke/BaselineLoader.cs:53-54`; `src/SchoolSearch.Import/Cke/CkeCsvSupport.cs:55` |
| I4 | Governing-body name column is never read (absence): only the type code is mapped | PASS | `src/SchoolSearch.Import/Sio/SioLoader.cs:30` (doc), `src/SchoolSearch.Import/Sio/SioColumnMap.cs:40` (type code only) |
| I5 | Error output: type and message only, no rows printed | PASS, note F10 | `src/SchoolSearch.Import/Program.cs:52` |
| I6 | Spreadsheet input has no size cap (operator-supplied file, ClosedXML) | NOT VERIFIED, low risk | `src/SchoolSearch.Import/Sio/SioLoader.cs` (ClosedXML) |
| I7 | Import not referenced by the Api; no HTTP path to it | PASS | `src/SchoolSearch.Api/SchoolSearch.Api.csproj` |

### 3.3 User-generated content: output and encoding

| # | Item | Result | Evidence |
|---|---|---|---|
| U1 | Correction text stored verbatim (control characters removed, edges trimmed), encoded at render | PASS | `src/SchoolSearch.Api/Corrections/CorrectionRequestValidator.cs:92` (`NormalizeMessage`), `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:109` |
| U2 | API JSON uses the default encoder, which escapes `<`, `>`, `&`, quotes | PASS | `src/SchoolSearch.Api/Errors/ApiError.cs:13` (`JsonSerializerDefaults.Web`) |
| U3 | Web renders user and API text as React text nodes; the only raw HTML is JSON-LD | PASS | `web/app/src/components/school-jsonld.tsx:32` (only `dangerouslySetInnerHTML` in `web/app`) |
| U4 | JSON-LD escapes `<`, `>`, `&`, U+2028 and U+2029 before embedding | PASS | `web/app/src/components/school-jsonld.tsx:4-12` |
| U5 | Links from API data (resolution, source) allow http and https only | PASS | `web/app/src/lib/validation.ts:37-46`; `web/app/src/components/coverage-badge.tsx:18`; `web/app/src/app/szkoly/[slug]/page.tsx:263` |
| U6 | Markdown rendering of user text | N/A | section 1 proof grep |
| U7 | Admin list returns message and contact email to the admin only; no admin UI exists in the web app | N/A now, F4 | `src/SchoolSearch.Api/Security/AdminEndpoints.cs` (`AdminCorrectionItem`) |
| U8 | Validation errors never echo the submitted value | PASS | `src/SchoolSearch.Api/Corrections/CorrectionRequestValidator.cs` (error text names the field only); `src/SchoolSearch.Api/Errors/ApiErrorMiddleware.cs:25` returns no exception text |
| U9 | Error responses use one shape everywhere (404, 405, 413, 429, 500) | PASS | `src/SchoolSearch.Api/Errors/ApiErrorMiddleware.cs:33-41`; `src/SchoolSearch.Api/Security/RequestBodyLimitMiddleware.cs:21`; `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:36` |

### 3.4 Rate limits

| # | Item | Result | Evidence |
|---|---|---|---|
| R1 | Anonymous fixed window per resolved client, 60 per minute by default | PASS in design, **FAIL in deployment (F1)** | `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:76-82`; `src/SchoolSearch.Api/Options/CatalogApiOptions.cs:21` |
| R2 | Process-wide concurrency bound (64) beneath the window | PASS | `src/SchoolSearch.Api/Options/CatalogApiOptions.cs:24`; `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:95` |
| R3 | Per-key bucket (300 per minute) keyed on the key digest, not the key | PASS | `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:65`; `src/SchoolSearch.Api/Security/SecurityOptions.cs:9` |
| R4 | Key issuance: 5 per IP per hour, own policy | PASS, inherits F1 | `src/SchoolSearch.Api/Security/SecurityServiceCollectionExtensions.cs` (policy); `src/SchoolSearch.Api/Security/SecurityOptions.cs:12` |
| R5 | Correction flood guard: 3 open requests per client, school and field per 24 hours | PASS, inherits F1 | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:25`, `:81` |
| R6 | Probes `/health` and `/alive` exempt | PASS | `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs` (`IsProbe`) |
| R7 | 429 carries Retry-After and the uniform body | PASS | `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:29-37` |
| R8 | Forwarded client address trusted only when configured | PASS in config, **FAIL in the BFF hop (F1)** | `src/SchoolSearch.Api/RateLimiting/ClientIpResolver.cs:13`, `:19-29`; `flyio/api.fly.toml:16`; `web/app/src/app/api/proxy/[...path]/route.ts:92-95` |
| R9 | Rate limiter chain order: key resolution before the limiter | PASS | `src/SchoolSearch.Api/Extensions/ServiceCollectionExtensions.cs:33-38` |

### 3.5 Correction requests: IDOR surface

| # | Item | Result | Evidence |
|---|---|---|---|
| C1 | Token from the CSPRNG, 16 bytes (128 bits), base64url, 22 characters, never a GUID or sequence | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:27-28`, `:190-194` |
| C2 | Unknown, malformed and wrong-length tokens share one 404 | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:133-134`, `:196-208` |
| C3 | Internal GUID is never returned or accepted as a lookup key | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs` (`CorrectionStatusResponse`, `CorrectionCreatedResponse`); test `tests/SchoolSearch.Api.Tests/Security/CorrectionTests.cs` (`An_internal_guid_is_not_a_lookup_key`) |
| C4 | No public listing or enumeration of corrections | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:34-35` (two routes only) |
| C5 | Admin routes take a GUID target behind the admin key; no per-user ownership exists to check | PASS for authorisation; residual: one shared credential, no audit (F3) | `src/SchoolSearch.Api/Security/AdminEndpoints.cs:43-44` |
| C6 | Status moves are a fixed transition table; terminal states cannot move | PASS | `src/SchoolSearch.Api/Security/AdminEndpoints.cs` (`Transitions`), `:134` |
| C7 | Token appears in the URL path and is therefore logged | FAIL, F5 | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:35`; `src/SchoolSearch.ServiceDefaults/Observability.cs:32-34` |
| C8 | Message and contact email never echoed by the status endpoint or the 201 body | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs` (response records); test `tests/SchoolSearch.Api.Tests/Security/CorrectionTests.cs` |

### 3.6 Admin key handling

| # | Item | Result | Evidence |
|---|---|---|---|
| A1 | Group answers 404 when `ADMIN_API_KEY` is unset, so its existence is not advertised | PASS | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:38-41` |
| A2 | Constant-time comparison over fixed-length SHA-256 digests | PASS | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:43-46` (`FixedTimeEquals`) |
| A3 | Key read from the `X-Admin-Key` header only (not the query string, not the body) | PASS | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:43`; test `tests/SchoolSearch.Api.Tests/Security/AdminEndpointTests.cs` |
| A4 | Missing or wrong key: identical uniform 401 | PASS | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:48-50`; test `tests/SchoolSearch.Api.Tests/Security/AdminEndpointTests.cs` |
| A5 | Admin responses carry `Cache-Control: no-store` | PASS | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:21-22` |
| A6 | Minimum length or entropy of the configured secret | **FAIL, F2** | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:23-27` (any non-blank string accepted) |
| A7 | Public key never grants admin | PASS | test `tests/SchoolSearch.Api.Tests/Security/AdminEndpointTests.cs` (`Public_api_key_is_not_an_admin_credential`) |
| A8 | No admin secret in the repository | PASS | `secrets.env.example:42` (empty placeholder) |

### 3.7 Random values (§5)

| # | Item | Result | Evidence |
|---|---|---|---|
| K1 | API key: 32 bytes from `RandomNumberGenerator`, `ss_` prefix, base64url (256 bits) | PASS | `src/SchoolSearch.Api/Security/ApiKeyService.cs` (`GenerateKey`, `KeyEntropyBytes = 32`) |
| K2 | Stored as lower-case SHA-256 hex; unsalted digest is acceptable for 256-bit random input | PASS, note | `src/SchoolSearch.Api/Security/ApiKeyService.cs` (`HashKey`) |
| K3 | Only the digest is persisted; the key is returned once and never logged | PASS | `src/SchoolSearch.Api/Security/ApiKeyService.cs:24-33`; `src/SchoolSearch.Api/Security/ApiKeyEndpoints.cs:45-47`; test `tests/SchoolSearch.Api.Tests/Security/ApiKeyTests.cs` |
| K4 | GUIDs only as identifiers, never as proof | PASS | `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:103` (`Id = Guid.NewGuid()`); `src/SchoolSearch.Api/Security/ApiKeyService.cs:21` |
| K5 | Password hashing | N/A (no passwords, ADR-0004) | section 1 |

### 3.8 Output, errors and rendering (§7) and authorisation structure (§8)

| # | Item | Result | Evidence |
|---|---|---|---|
| E1 | Unhandled exceptions log the route template only, never the raw path or body | PASS | `src/SchoolSearch.Api/Errors/ApiErrorMiddleware.cs:20-22` |
| E2 | Exception text is never returned | PASS | `src/SchoolSearch.Api/Errors/ApiErrorMiddleware.cs:25` |
| E3 | Swagger/OpenAPI off in production | **FAIL, F6** | `src/SchoolSearch.ServiceDefaults/ServiceDefaultsExtensions.cs:55`; `flyio/api.fly.toml:13` |
| E4 | Ownership checks | N/A (no user-owned resources; corrections are capability tokens) | section 2 |
| E5 | CORS origins explicit; fail closed when none configured | PASS | `src/SchoolSearch.ServiceDefaults/CorsPolicies.cs:26-30` |
| E6 | CORS allows GET, HEAD, OPTIONS only; no credentials | PASS | `src/SchoolSearch.ServiceDefaults/CorsPolicies.cs:35` |
| E7 | Production origins configured on purpose: none, because browsers only call the same-origin BFF | PASS (by design) | `flyio/api.fly.toml:21-22` (comment) |
| E8 | Deny by default: explicit public list; admin group filtered as a whole | PASS | section 2; `src/SchoolSearch.Api/Security/AdminEndpoints.cs:40` |
| E9 | BFF checks Origin on the one write route | PASS | `web/app/src/app/api/proxy/[...path]/route.ts` (`originAllowed`) |
| E10 | Cookies and Authorization never forwarded or set by the BFF | PASS | `web/app/src/app/api/proxy/[...path]/route.ts:4` (comment), `:97-103` |

### 3.9 Headers

| # | Item | Result | Evidence |
|---|---|---|---|
| H1 | `X-Content-Type-Options: nosniff` | PASS | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:16` |
| H2 | `Referrer-Policy` | PASS | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:17` (`no-referrer`) |
| H3 | `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` | PASS | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:10`, `:18` |
| H4 | `X-Frame-Options` | **FAIL, F7** (CSP `frame-ancestors` covers modern browsers) | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:16-18` |
| H5 | `Permissions-Policy` | **FAIL, F7** (JSON API; low impact) | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:16-18` |
| H6 | Web frontend sends the full guide set (CSP, nosniff, DENY, referrer, permissions) | PASS | `web/app/next.config.ts:27-37` |
| H7 | HSTS | PASS at the edge (`force_https`) | `flyio/api.fly.toml` (`http_service`) |
| H8 | `Cache-Control: no-store` on key issuance and admin | PASS | `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:19-22` |

### 3.10 Request body limits

| # | Item | Result | Evidence |
|---|---|---|---|
| B1 | Declared Content-Length over 16 KB refused with 413 before the handler | PASS | `src/SchoolSearch.Api/Security/RequestBodyLimitMiddleware.cs:14`, `:24-27` |
| B2 | Chunked bodies capped by the Kestrel per-request limit | PASS | `src/SchoolSearch.Api/Security/RequestBodyLimitMiddleware.cs:29-31` |
| B3 | BFF refuses bodies over 16 KB | PASS | `web/app/src/app/api/proxy/[...path]/route.ts:19` |

### 3.11 Secrets in logs and telemetry

| # | Item | Result | Evidence |
|---|---|---|---|
| L1 | API key, correction message and email never logged | PASS | no logger calls in `src/SchoolSearch.Api/Security/ApiKeyEndpoints.cs` or `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs`; `src/SchoolSearch.Api/Security/ApiKeyResolutionMiddleware.cs:32` logs the exception type only |
| L2 | Admin key never logged | PASS | `src/SchoolSearch.Api/Security/AdminKeyFilter.cs` (no logger) |
| L3 | Token and path in traces | **FAIL, F5** | `src/SchoolSearch.ServiceDefaults/Observability.cs:32-34` |
| L4 | Migration failures log the exception object | PASS, note | `src/SchoolSearch.Api/Hosting/MigrationHostedService.cs:55` |
| L5 | Import CLI prints exception type and message | PASS, note F10 | `src/SchoolSearch.Import/Program.cs:52` |
| L6 | Health body lists integration names, states and details to anonymous callers | FAIL, F8 | `src/SchoolSearch.ServiceDefaults/HealthChecks.cs:54-58` |

### 3.12 Dependency and supply-chain state

| # | Item | Result | Evidence |
|---|---|---|---|
| D1 | NuGet audit on, mode all (transitive included), level low; restore fails on known vulnerabilities | PASS (configuration); NOT VERIFIED (no restore run here) | `Directory.Build.props:13-15` |
| D2 | Automated dependency updates | **FAIL, F11** (Dependabot declared off) | `.github/dependabot.yml:2-13`; ADR-0005 |
| D3 | Web dependency audit in CI | **FAIL, F11** (no `audit` step in `.github/workflows/ci.yml`) | `.github/workflows/ci.yml` (grep `audit`: none) |
| D4 | Secret scanning and code scanning workflows exist | PASS (presence only; contents not reviewed here) | `.github/workflows/secret-scan.yml`, `.github/workflows/codeql.yml`; `.gitleaks.toml` |
| D5 | Packages pinned centrally, no version attributes in projects | PASS | `Directory.Packages.props` |

### 3.13 Injection and other classes

| # | Item | Result | Evidence |
|---|---|---|---|
| J1 | SQL injection | PASS (no raw SQL; EF parameterised) | section 1 |
| J2 | XML external entities | N/A (no XML parsed on HTTP) | section 1 |
| J3 | SSRF | PASS (no outbound fetch driven by request input; BFF uses configured candidates) | `web/app/src/lib/catalog-url.ts` (configured); route allowlist `web/app/src/app/api/proxy/[...path]/route.ts:24-36` |
| J4 | CSRF | N/A for the API (no cookies); BFF checks Origin | `web/app/src/app/api/proxy/[...path]/route.ts` (`originAllowed`) |
| J5 | Deserialisation | PASS (System.Text.Json into fixed types; `UnmappedMemberHandling` disallowed for maps) | `src/SchoolSearch.Api/Security/JsonBodyReader.cs`; `src/SchoolSearch.Import/Common/ColumnMapLoader.cs:16` |
| J6 | Enumeration of schools | Intended (public catalogue) | `src/SchoolSearch.Api/Search/SearchEndpoints.cs:42-43` |

### 3.14 Metric and anti-goal absences (cross-check with METRIC-ETHICS)

| # | Item | Result | Evidence |
|---|---|---|---|
| M1 | No ascending or bottom ranking; no direction parameter | PASS | `src/SchoolSearch.Api/Scoring/RankingBuilder.cs:9`; test `tests/SchoolSearch.Architecture.Tests/AntiGoalTests.cs`; test `tests/SchoolSearch.Api.Tests/Security/OpenApiSortParameterTests.cs` |
| M2 | Suppressed schools never ranked and never placed at the bottom | PASS | `src/SchoolSearch.Api/Scoring/RankingBuilder.cs:14-15`, `:49-51`; test `tests/SchoolSearch.Api.Tests/Public/RankingContractTests.cs` |
| M3 | Every score carries cohort size and methodology version | PASS | `src/SchoolSearch.Contracts/Public/ScoreBlock.cs`; test `tests/SchoolSearch.Architecture.Tests/AntiGoalTests.cs` (ScoreBlock rule) |
| M4 | Operator name never modelled or returned | PASS | `src/SchoolSearch.Contracts/Public/SchoolProfile.cs:26`; `src/SchoolSearch.Catalog.Data/Entities/School.cs` (remarks); test `tests/SchoolSearch.Architecture.Tests/AntiGoalTests.cs` |
| M5 | No teacher, pupil or nationality entity or member (aggregate counts allowed by an explicit list) | PASS | test `tests/SchoolSearch.Architecture.Tests/AntiGoalTests.cs` |
| M6 | Catchment never synthesised from distance | PASS | `src/SchoolSearch.Api/Catchment/CatchmentEndpoints.cs:12-14` (doc), `:113-118` |

## 4. Findings

Format per SECURITY-REVIEW §2. Severity and priority per §3.

### F1. Shared anonymous rate-limit bucket behind the BFF. High. P1 (blocks deployment). OPEN.

- **Location:** `web/app/src/app/api/proxy/[...path]/route.ts:92-95`; `src/SchoolSearch.Api/RateLimiting/ClientIpResolver.cs:13`, `:19-29`; `src/SchoolSearch.Api/RateLimiting/RateLimitingExtensions.cs:76-82`; `flyio/api.fly.toml:16`.
- **Issue:** The BFF forwards the first entry of `x-forwarded-for`. The API resolves the client only from `Fly-Client-IP`, and the BFF never sends that header. Every proxied request therefore carries the web machine's socket address as its client.
- **Current code:** `if (clientIp) headers["x-forwarded-for"] = clientIp.split(",")[0]?.trim() ?? "";` (web/app/src/app/api/proxy/[...path]/route.ts:95), against `private const string FlyClientIpHeader = "Fly-Client-IP";` (ClientIpResolver.cs:13).
- **Risk:** High.
- **Attack scenario:** One visitor sends more than 60 requests in a minute to `/api/v1/schools` through the site. Every other visitor's next request gets 429 until the window resets. For corrections, the flood-guard key is a salted hash of the shared address plus school plus field, so the whole site gets three correction submissions per school and field per day.
- **Impact:** Site-wide denial of service for anonymous traffic from one client, and the anonymous limiter does not do the job it is configured for. If the API were later changed to read `x-forwarded-for` as the guide suggests, the first entry is client-supplied, so an attacker could pick their own bucket.
- **Recommendation:** The BFF forwards the `fly-client-ip` header from the inbound request (set only by the Fly edge) and does not derive the client from `x-forwarded-for`. Keep `SchoolSearch__TrustProxyClientIpHeader=true`. Add a BFF test that a request with a forged `x-forwarded-for` does not change the bucket.
- **Better:** Use the Fly private network with a service-to-service identity, so the API trusts the BFF's identity rather than a header value.
- **Test status:** `tests/SchoolSearch.Api.Tests/Security/RateLimitTests.cs` (`Fly_Client_IP_header_is_ignored_when_no_proxy_is_configured`) covers the API side only. The BFF side is not tested here.

### F2. ADMIN_API_KEY has no minimum length or entropy. Medium. Before deployment (P2). OPEN.

- **Location:** `src/SchoolSearch.Api/Security/AdminKeyFilter.cs:23-27`.
- **Issue:** `FromSecret` accepts any non-blank string.
- **Attack scenario:** The operator sets a memorable value. An attacker brute-forces `X-Admin-Key` at up to 60 requests per minute per IP (and, while F1 stands, the bound is not per attacker).
- **Impact:** Full correction moderation and key revocation.
- **Recommendation:** Require at least 32 characters, fail fast on startup when shorter in Production, and document a generation command in `flyio/SECRETS.md`.

### F3. Admin actions leave no audit record. Medium. Before deployment (P2). OPEN.

- **Location:** `src/SchoolSearch.Api/Security/AdminEndpoints.cs:134-136` (status change sets `Status`, `StatusReason`, `UpdatedAt`); `:163` (revocation sets `RevokedAt`).
- **Issue:** No record of who acted or with which key. `UpdatedAt` is the only trace, and it is overwritten on the next change.
- **Attack scenario:** A leaked admin key is used to accept or reject corrections or revoke keys. Nothing shows when or how often this happened.
- **Impact:** Misuse cannot be detected or reconstructed.
- **Recommendation:** Append-only `AdminAction` rows (time, action, target id, a short fingerprint of the key digest). No secrets and no personal data in the row.

### F4. Correction contact emails and free text have no retention or erasure path. Medium. Before deployment (P2). OPEN.

- **Location:** `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:109` (persisted); `src/SchoolSearch.Catalog.Data/Entities/CorrectionRequest.cs` (`ContactEmail`, `Message`); `AdminCorrectionItem` returns both to the admin.
- **Issue:** Personal data is kept indefinitely. There is no job that deletes or anonymises it and no erasure path.
- **Attack scenario:** A database dump or a backup restore exposes every submitter's email.
- **Impact:** Privacy exposure and an unanswerable erasure request.
- **Recommendation:** Erase `ContactEmail` a fixed period (for example 90 days) after the request closes, and on request. State the period in the privacy notice.

### F5. Correction token is in the URL path, so it reaches logs and traces. Low. P3. OPEN.

- **Location:** `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:35` (`/{token}`); `src/SchoolSearch.ServiceDefaults/Observability.cs:32-34` (ASP.NET Core instrumentation); proxy access logs.
- **Attack scenario:** Someone with read access to traces or proxy logs collects tokens and checks the status of other people's requests.
- **Impact:** Limited. The status response carries status, reason, date, school slug and field, with no message and no email. It is still a bearer capability leaking.
- **Recommendation:** Accept as a documented residual, or move the token to a header or body. At minimum, drop `url.path` for the corrections route from traces.

### F6. OpenAPI document is served in production. Low. Before deployment (P2). OPEN.

- **Location:** `src/SchoolSearch.ServiceDefaults/ServiceDefaultsExtensions.cs:55` (`app.MapOpenApi()` unconditional); `flyio/api.fly.toml:13` (`ASPNETCORE_ENVIRONMENT = "Production"`).
- **Issue:** SECURITY-REVIEW §7 says the OpenAPI UI is off in production. Here the document itself is public.
- **Impact:** The route map and DTO shapes are disclosed. The shapes are also public through the contract, so the added exposure is small.
- **Recommendation:** Map OpenAPI only outside Production, or behind an explicit flag.

### F7. API omits X-Frame-Options and Permissions-Policy. Low. P3. OPEN.

- **Location:** `src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:16-18`.
- **Impact:** Low. CSP `frame-ancestors 'none'` covers modern browsers, and the API serves JSON. The web app sends both (`web/app/next.config.ts:33-36`), so the API differs from the frontend set.
- **Recommendation:** Add `X-Frame-Options: DENY` and `Permissions-Policy` with empty values. The test `HeadersAndBodyLimitTests` should then assert them.

### F8. Health body discloses integration detail to anonymous callers. Low. P3. OPEN.

- **Location:** `src/SchoolSearch.ServiceDefaults/HealthChecks.cs:54-58`.
- **Issue:** Integration names, states and free-text details (for example "postgres selected but no connection string") are returned publicly.
- **Impact:** Configuration fingerprinting. Small.
- **Recommendation:** Public `/health` returns the status only. Put details in logs or behind an internal route.

### F9. Correction submissions are serialised through one process-wide gate. Low. P3. OPEN.

- **Location:** `src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:81` (`guard.Gate.WaitAsync`); `:254` (`SemaphoreSlim(1, 1)`).
- **Attack scenario:** A few slow database writes stall every correction submission in the process.
- **Impact:** Availability for one feature.
- **Recommendation:** Serialise per client key, or enforce the open-request count with a database constraint in one transaction.

### F10. Import CLI accepts any path and prints exception messages. Low (operator only). P4. OPEN.

- **Location:** `src/SchoolSearch.Import/Program.cs:101`, `:114`, `:126` (`File.OpenRead(file)`); `:52` (`ex.Message` printed).
- **Attack scenario:** None over HTTP. An operator error can print a path or a provider message to the console.
- **Recommendation:** Print the exception type for database errors. Keep the CLI off any HTTP surface, which is already the case.

### F11. Dependency updates are manual and the web audit is not in CI. Medium (process). Before deployment (P2). OPEN.

- **Location:** `.github/dependabot.yml:13` (`open-pull-requests-limit: 0`), ADR-0005; `.github/workflows/ci.yml` (no `audit` step); `Directory.Build.props:13-15` (NuGet audit only).
- **Impact:** Known vulnerabilities in web packages are found only when someone looks. The NuGet audit fails restore but does not report what is outdated.
- **Recommendation:** Enable Dependabot security updates only (not version bumps), and add a `pnpm audit --prod` job to CI.

### F12. Well-formed unknown API keys cost one database query each, ahead of the limiter. Low. P3. OPEN.

- **Location:** `src/SchoolSearch.Api/Security/ApiKeyService.cs:56` (`AnyAsync`); `ApiKeyResolutionMiddleware` runs before `UseRateLimiter` (`src/SchoolSearch.Api/Extensions/ServiceCollectionExtensions.cs:37-38`).
- **Attack scenario:** A flood of random well-formed keys costs one query each, before the limiter rejects the request.
- **Recommendation:** Cache negative lookups briefly, or apply the anonymous window before key resolution.

## 5. Readiness ledger (§3)

Legend: OPEN, FIXED, NOT APPLICABLE. Context column = what already exists that makes the fix cheap.

| ID | Priority | Status | Evidence | Problem | Context | Action |
|---|---|---|---|---|---|---|
| F1 | P1 | OPEN | web/app/src/app/api/proxy/[...path]/route.ts:92-95; src/SchoolSearch.Api/RateLimiting/ClientIpResolver.cs:13 | Shared anonymous bucket behind the BFF | Edge already sets Fly-Client-IP; resolver already gated by config | BFF forwards `fly-client-ip`; add a BFF test |
| F2 | P2 | OPEN | src/SchoolSearch.Api/Security/AdminKeyFilter.cs:23-27 | No minimum admin secret strength | Digest comparison already in place | Length check and startup failure in Production |
| F3 | P2 | OPEN | src/SchoolSearch.Api/Security/AdminEndpoints.cs:134-136, :163 | No admin audit trail | Admin filter already a single point | Append-only AdminAction table |
| F4 | P2 | OPEN | src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:109 | Emails kept indefinitely | Timestamps exist | Erase after closed status plus period |
| F6 | P2 | OPEN | src/SchoolSearch.ServiceDefaults/ServiceDefaultsExtensions.cs:55 | OpenAPI served in Production | Environment already known | Map only outside Production |
| F11 | P2 | OPEN | .github/dependabot.yml:13; .github/workflows/ci.yml | Manual dependency updates, no web audit | NuGet audit in place | Security-only Dependabot; `pnpm audit` job |
| F5 | P3 | OPEN | src/SchoolSearch.ServiceDefaults/Observability.cs:32-34 | Tokens in traces | Metrics already use route templates | Drop url.path for corrections |
| F7 | P3 | OPEN | src/SchoolSearch.Api/Security/SecurityHeadersMiddleware.cs:16-18 | Two headers missing | Web set already defined | Add two headers, assert in test |
| F8 | P3 | OPEN | src/SchoolSearch.ServiceDefaults/HealthChecks.cs:54-58 | Health detail public | Registry already holds detail | Status only on public route |
| F9 | P3 | OPEN | src/SchoolSearch.Api/Corrections/CorrectionEndpoints.cs:81 | Process-wide gate | Count check already in code | Per-key lock or constraint |
| F12 | P3 | OPEN | src/SchoolSearch.Api/Security/ApiKeyService.cs:56 | Lookup before limiter | Limiter already keyed on digest | Negative cache or order change |
| F10 | P4 | OPEN | src/SchoolSearch.Import/Program.cs:52 | Operator CLI prints detail | Operator-only | Print type for database errors |

**Blocks deployment:** F1.

**Should be done before deployment:** F2, F3, F4, F6, F11.

## 6. Positive findings

- No raw SQL, and the only text filter is a parameterised, escaped `ILike`.
- Correction tokens and API keys come from a CSPRNG at the sizes the guide requires; keys are stored as digests only.
- Unknown, malformed and wrong-length correction tokens share one 404, and the internal GUID is never a lookup key.
- The admin group is filtered as a whole, returns 404 when unconfigured, and compares digests in constant time.
- No secret, token, message or email is written to logs; the error middleware logs the route template only.
- JSON-LD escapes script-breaking characters; API links are restricted to http and https; the web app has no raw-HTML path except JSON-LD.
- The web BFF forwards only allowlisted routes, refuses admin paths, sends no cookies, and checks Origin on the one write.
- The Import CLI refuses person-data headers before reading any row and never reads the governing-body name column.
- Scoring suppresses below the cohort threshold and never ranks suppressed schools; the counter-metric is blended into the score.
- The anonymous limit and key-issuance limit are both configured by options, not hard-coded.

## 7. Residual risks (accepted for launch, to be revisited)

- **Key issuance is anonymous.** Five keys per IP per hour, but IP rotation removes that bound. Accepted: keys carry only a 300-per-minute bucket and no data access beyond the public catalogue.
- **Correction flood guard is in memory and per process.** It resets on restart and is not shared across machines. Accepted while the API runs on one machine (`flyio/api.fly.toml`, `min_machines_running = 1`). Revisit before scaling out.
- **No multi-factor authentication for the admin credential.** Accepted: single operator, no accounts until Phase E. Revisit with Phase E.
- **Token-in-URL (F5)** accepted as a documented residual until the logging change lands.
- **Unsalted SHA-256 for API keys.** Accepted because the input is 256 random bits, not a password.

## 8. Test coverage for these controls

Tests added in this phase (not compiled or run here, to be confirmed in CI):

- `tests/SchoolSearch.Architecture.Tests/AntiGoalTests.cs`: person-concept names, operator-name members, ascending or bottom names, and the ScoreBlock rule, across every production assembly named in the brief plus ServiceDefaults.
- `tests/SchoolSearch.Api.Tests/Security/RateLimitTests.cs`: 429 with Retry-After and the uniform body; probe exemption; forged `Fly-Client-IP` ignored; key-issuance policy.
- `tests/SchoolSearch.Api.Tests/Security/ApiKeyTests.cs`: 256-bit key shape; digest-only storage checked column by column; unknown keys anonymous; label validation.
- `tests/SchoolSearch.Api.Tests/Security/AdminEndpointTests.cs`: 404 when unset; uniform 401; 200 with the right key; no query-string key; public key is not admin; no-store.
- `tests/SchoolSearch.Api.Tests/Security/CorrectionTests.cs`: token shape and randomness; no echo; uniform 404; GUID not a key; 16 KB limit with nothing stored; field and email validation.
- `tests/SchoolSearch.Api.Tests/Security/HeadersAndBodyLimitTests.cs`: nosniff, Referrer-Policy, CSP on JSON and error paths; no-store on admin; 413 on oversize bodies.
- `tests/SchoolSearch.Api.Tests/Security/OpenApiSortParameterTests.cs`: no sort-direction parameter or body property in `/openapi/v1.json`.
- `tests/SchoolSearch.Api.Tests/Public/*.cs`: exact contract keys, score arithmetic, suppression, ranking order and ties, the counter-metric end to end, catchment `no_data`, `no_match` and `street_list_match`.

## 9. Caveat

This is a static review and a test suite. It has not been run against a deployed system and does not replace a penetration test before the first public launch.
