# Runbook: Billing service (premium profiles, enquiries, subscriptions)

The Billing service (`src/SchoolSearch.Billing`, Fly app `school-search-billing-dev`, database `billingdb`) owns
entitlements for the Phase F products (ADR-0016). This runbook covers the decisions that must be taken before a real sale,
the database and secrets, the deploy, the webhook go-live checks, and the rollback.

**No key, secret or payment detail belongs in this file, in a commit, in an issue, in a chat or in a log.** Secrets live in
the GitHub environment `dev` and in Fly secrets (`flyio/SECRETS.md`). Agents never deploy. Deploys come from a `v*` tag in CI.

**Current state:** no payment provider is chosen. The service runs in mock mode outside Production and without a provider
in Production. Checkout answers 503 `billing_unavailable`. No sale is possible until section 5 is complete.

## 0. Preconditions

- The Billing project and its Dockerfile are on the branch (`src/SchoolSearch.Billing/SchoolSearch.Billing.csproj` and
  `src/SchoolSearch.Billing/Dockerfile`). The deploy job selects billing only when both files exist.
- authservice is deployed and its issuer and audience match (`flyio/authservice.fly.toml`, `flyio/billing.fly.toml`).
  Billing validates authservice tokens and holds no user table.
- `school-search-postgres` exists (one instance, `--ha=false`). Billing shares it with the other services, as recorded in
  the deviation register.

## 1. Decide the payment provider (owner, before any sale)

ADR-0016 records this as **Pending**. Before any code is written for a provider adapter, the owner records in `docs/`:

| Decision | What to record | Source |
|---|---|---|
| Merchant of record or payment gateway | The choice, and why. A gateway makes us responsible for EU VAT, chargebacks and PCI scope. The guide's default is a merchant of record for a small team selling globally | `docs/guides/PAYMENTS-AND-MONETIZATION.md` section 2 (architecture-standards) |
| The provider | Its name, its test account, its fees with numbers. **NOT FOUND in any source**: the numbers come from the provider's price list, on the day of the decision | Owner |
| Hosted checkout | Assumed (card data never reaches this service). The guide states it as an inference, not a rule | MONETIZATION-BRIEF section 1b |
| Webhook signature header name | Not known. Confirm against sandbox traffic (section 4). The code default is `X-Signature` and is not confirmed | Provider documentation and sandbox |
| Event taxonomy and spelling | Not known. The mapper must absorb spelling variants (`cancelled` and `canceled`) and log unknown names | Provider documentation and sandbox |

Record the decision in a new ADR or in ADR-0016 (update its status from Pending). Cite P14. Until then, do not add provider
code, a checkout host or a provider name to the repository.

## 2. Create the database (once)

One Postgres instance holds one database per service. CI does not create databases. The Billing database is created by hand,
as for `reviewsdb` and `authservicedb` (`docs/runbooks/AUTHSERVICE.md` section 2).

```bash
flyctl proxy 15432:5432 --app school-search-postgres &
read -r -s -p "POSTGRES_PASSWORD: " PGPASSWORD; echo; export PGPASSWORD
psql "host=localhost port=15432 user=schoolsearch dbname=catalogdb" \
  -c "CREATE DATABASE billingdb;"
unset PGPASSWORD
kill %1
```

If the database exists, `CREATE DATABASE` fails with "already exists". That is the expected answer on a rerun. Verify the
proxy syntax against your flyctl version before running it.

The migration `20261027000000_Billing_Initial` creates the tables when the service starts. If the database is missing, the deploy health
check fails. That is the intended failure point.

## 3. Secrets and deploy

GitHub environment `dev` (inputs to CI):

| Name | Type | Required | Notes |
|---|---|---|---|
| `BILLING_SUBJECT_PEPPER` | secret | Yes, the deploy job refuses without it | `openssl rand -hex 32`. Different from `REVIEWS_SUBJECT_PEPPER`. Never change it once claims or subscriptions exist |
| `BILLING_WEBHOOK_SECRET` | secret | No, until a provider is chosen | The provider's signing secret, from its dashboard. Without it every webhook is rejected in Production |
| `POSTGRES_PASSWORD` | secret | Yes (already required) | Assembles `ConnectionStrings__billingdb` on Fly |
| `FLY_API_TOKEN` | secret | Yes (already required) | |

Public values in `flyio/billing.fly.toml` (`[env]`): `Billing__MockMode = "false"`, `Auth__Authority`, `Auth__Issuer`,
`Auth__Audience`. The web app gets `BILLING_BASE_URL` in `flyio/web.fly.toml`.

Deploy path:

1. Merge the branch to `main` after the CI checks pass (`ci.yml`, including the Billing image smoke test on :8083 and the
   e2e run that starts Billing in mock mode).
2. Set the GitHub `dev` secret `BILLING_SUBJECT_PEPPER` before the tag. Set `BILLING_WEBHOOK_SECRET` only when section 1 is done.
3. Push a `v*` tag. The `deploy billing (Phase F)` job:
   - creates the app if missing;
   - imports `ConnectionStrings__billingdb` and `Billing__SubjectPepper` with `flyctl secrets import --stage`, on stdin
     (and `Billing__WebhookSecret` when the GitHub secret is set; otherwise it prints a warning);
   - deploys `flyio/billing.fly.toml` with the image built in the same run (no rebuild);
   - checks `GET /health` (retried for up to about 100 seconds).
4. `deploy web` waits for billing. A failed billing deploy stops the web deploy, and the web app keeps its previous version.

## 4. Webhook go-live checks (before the first real payment)

Every production webhook is rejected until these hold. This is the correct failure (MONETIZATION-BRIEF 1j): a wrong header
name or an unset secret rejects payments and does not grant access.

1. **Header name.** Send one sandbox event from the provider. Confirm the header that carries the signature, and set
   `Billing__SignatureHeader` only after confirming it. Do not guess it from the code default.
2. **Signature.** Confirm the provider signs the raw body with HMAC-SHA256 and the hex format the service expects. A
   mismatch is a 401, not a 500. Check the log line for the event id and the outcome, never the body.
3. **Event taxonomy.** Compare the real event names with the mapper. Each lifecycle event (`payment_succeeded`,
   `subscription_cancelled`, `subscription_ended`, `renewal_failed`) must have an explicit branch. Unknown names are logged
   with `Outcome=unknown_type`. Replace the `GenericJsonEventMapper` placeholder with the provider adapter before go-live.
4. **Replay.** Send the same event twice. The second delivery must be a recorded no-op (`WebhookEvent.EventId`).
5. **Order.** If a payment event arrives before the checkout row, the service synthesises the subscription
   (`Outcome=order_synthesised`). It must not drop the event.
6. **Production mode.** Confirm `GET /api/v1/billing/simulate` answers 403 on the deployed service, with a verified user token.

## 5. Owner and legal decisions (go-live checklist)

Do not sell until every item is answered. Each open item is listed in the legal drafts with its owner.

- [ ] Provider and cost decision recorded (section 1; ADR-0016 status updated).
- [ ] Webhook header name and taxonomy confirmed (section 4), and the placeholder mapper replaced.
- [ ] Plans configured (`Billing:Plans`: code, name, price, interval). Empty means "Cennik w przygotowaniu."
- [ ] Legal drafts approved by a lawyer: `docs/legal/SALES-TERMS-DRAFT.pl.md` (withdrawal right, price display, VAT, complaints,
      seller identity) and `docs/legal/ENQUIRY-PRIVACY-DRAFT.pl.md` (controller, legal basis, withdrawal of consent).
- [ ] Verification procedure for claims written down and followed by the editors (outside the system).
- [ ] Owner's contact point for complaints and consumer requests set. Not invented in code.
- [ ] `docs/security/SECURITY-REVIEW-PHASE-F.md` NOT RUN items run or accepted by the owner.

## 6. Post-deploy checks

Read-only checks, safe to repeat:

```bash
curl -sS https://school-search-billing-dev.fly.dev/health
curl -sS https://school-search-billing-dev.fly.dev/api/v1/plans
```

- `/health` answers 200 and lists `authservice` and `billing-provider`. `billing-provider` is disabled while mock mode is on or
  no webhook secret is set. Disabled is the expected state before section 4.
- `/api/v1/plans` answers `{"plans":[],"note":"Cennik w przygotowaniu."}` while `Billing:Plans` is empty.

Do not call the simulate or checkout endpoints against Production to test them. They change state.

## 7. Rollback and incident response

- **Bad release.** Redeploy the previous image tag with `flyctl deploy --config flyio/billing.fly.toml --image <previous tag>`,
  as the owner, from a trusted terminal. The database is not rolled back, and no data is deleted.
- **Suspected forged or wrong grants.** Remove the webhook secret with `flyctl secrets unset Billing__WebhookSecret --app
  school-search-billing-dev`. Every webhook is then rejected (fail closed). Investigate the `WebhookEvent` rows for the
  affected school. Record the incident in `docs/security/`.
- **Leaked webhook secret.** Rotate it at the provider, then set the new value with `flyctl secrets import --stage`, on stdin,
  never on the command line.
- **Leaked subject pepper.** Treat it as a privacy incident (RODO). A pepper change breaks every stored subject hash. Do not
  rotate it silently. Record the decision in an ADR.
- **Expired subscriptions.** The service locks profiles with `LockedReason = subscription_expired`. It never deletes them.
  Renewal unlocks. Do not delete rows by hand.

## 8. Open points

| Ref | Open point | How to close it | Consequence until closed |
|---|---|---|---|
| B1 | Payment provider, merchant of record or gateway, cost | Section 1 | No sale; checkout 503 |
| B2 | Webhook signature header name | Section 4.1 | Production webhooks rejected |
| B3 | Event taxonomy and spelling variants | Section 4.3 | Unknown events logged and ignored |
| B4 | Outbound idempotency keys for provider calls, refunds | Provider documentation | NOT FOUND in the guide; not designed |
| B5 | Consumer law, VAT invoices, price display, seller identity | Section 5, `SALES-TERMS-DRAFT.pl.md` | Not sold |
| B6 | Controller, legal basis and consent withdrawal for enquiries | Section 5, `ENQUIRY-PRIVACY-DRAFT.pl.md` | Enquiries stay off |
| B7 | Replay window of the provider's signed events | Provider documentation | Replays are accepted only as duplicates |
| B8 | Minimum machines: Billing is pinned to one machine because the school page reads it in-request | Owner's cost decision (`flyio/billing.fly.toml`) | Billing and Reviews each keep one machine running |

## 9. Local development seed

For a local run, the AppHost seeds this service through `POST /dev/seed`. The full picture (the AppHost, the seeder and the
accounts) is in [`LOCAL-DEV.md`](LOCAL-DEV.md). This section covers only the endpoint.

- **Route:** `POST /dev/seed` on the Billing service. Anonymous, with the key header below.
- **Gate:** the route exists only when all three hold: `DevSeed:Enabled` is `true` (env `DevSeed__Enabled`), `DevSeed:Key`
  is non-empty (env `DevSeed__Key`), and the environment is not Production. Otherwise the answer is `404`.
- **Key:** header `X-Dev-Seed-Key: <key>`, compared in constant time. A missing or wrong key answers `401` and writes nothing.
  The AppHost generates the key for each run. Never put it in a file, a commit or a log.
- **Request:** `{"subjects": {"owner": "<sub>", "moderator": "<sub>"}}`. `owner` is required (`400` without it). `moderator`
  is optional: without it the approved claim has no `decidedBy`. Subjects are at most 200 characters. The service hashes them
  with its own pepper, as normal writes do.
- **Response:** `200` with `{"claims", "subscriptions", "profiles", "enquiries", "alreadySeeded"}`.
- **What it seeds (all synthetic, labelled DEMO, reserved `.test` and `.invalid` domains, no real school or person):**
  - an approved claim by the owner on `demo-sp-1`, with an active subscription and a published sponsored profile (plain
    text, marked as sponsored content);
  - a pending claim by the owner on `demo-sp-2`, for the moderator queue;
  - several synthetic parent enquiries in the owner's inbox, one of them already answered.
- **Mock mode only:** the subscription row carries the provider reference `mock-demo-seed` and no provider is called. The
  endpoint does not read `Billing:MockMode`; the gate above is what keeps it off. Never enable it on a deployed environment.
- **Plan code:** the subscription shows the plan code `demo-plan`. It is not an entry of `Billing:Plans`, because no plan code
  is configured in the repository. The owner sees it as-is. Entitlement does not read it.
- **Idempotent:** the approved claim with its fixed id is the marker. A repeat call writes nothing and answers
  `alreadySeeded: true`. The seed is one `SaveChanges`, so a failure stores nothing.
