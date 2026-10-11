"use client";

import Link from "next/link";
import { useEffect, useId, useState } from "react";
import { EnquiryInbox } from "@/components/billing/enquiry-inbox";
import { ProfileEditor } from "@/components/billing/profile-editor";
import {
  billingFeedback,
  CLAIM_STATUS_LABEL,
  INTERVAL_LABEL,
  SUBSCRIPTION_STATUS_LABEL,
} from "@/components/billing/billing-messages";
import { SessionGate } from "@/components/billing/session-gate";
import { bffRequest, type BffResponse } from "@/lib/bff-client";
import { parseMyClaims, parsePlans, parsePremium } from "@/lib/billing-parse";
import type { MyClaimView, PlansEnvelope, PremiumEnvelope } from "@/lib/billing-types";
import { formatDatePl, formatDecimal } from "@/lib/format";
import { safeHttpUrl } from "@/lib/validation";

/**
 * Owner area of one school: claim status, plan and checkout, cancel or resume, the sponsored profile editor and the
 * enquiry inbox. Payment happens at the provider (hosted checkout); card data never reaches this page. In test mode
 * the owner can simulate a successful payment, which runs the same server code path as the provider webhook.
 */

interface Loaded {
  readonly claims: readonly MyClaimView[];
  readonly plans: PlansEnvelope | null;
  readonly premium: PremiumEnvelope | null;
}

interface Pending {
  readonly subscriptionId: string;
  readonly paymentUrl: string | null;
}

export function OwnerSchoolPanel({ slug }: { readonly slug: string }) {
  return (
    <SessionGate requireVerified>
      <Panel slug={slug} />
    </SessionGate>
  );
}

function Panel({ slug }: { readonly slug: string }) {
  const planId = useId();
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [planCode, setPlanCode] = useState("");
  const [pending, setPending] = useState<Pending | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: "error" | "done"; text: string; detail: string | null } | null>(null);

  async function load(): Promise<void> {
    const [claimsResponse, plansResponse, premiumResponse] = await Promise.all([
      bffRequest("/api/proxy/api/v1/claims/mine"),
      bffRequest("/api/proxy/api/v1/plans"),
      bffRequest(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/premium`),
    ]);
    const claims = claimsResponse?.status === 200 ? parseMyClaims(claimsResponse.body) : null;
    if (claims === null) {
      setFailed(billingFeedback(claimsResponse, "Nie udało się pobrać stanu konta szkoły.").text);
      return;
    }
    const plans = plansResponse?.status === 200 ? parsePlans(plansResponse.body) : null;
    const premium = premiumResponse?.status === 200 ? parsePremium(premiumResponse.body) : null;
    setFailed(null);
    setLoaded({ claims: claims.filter((claim) => claim.schoolSlug === slug), plans, premium });
    if (plans && plans.plans.length > 0) {
      setPlanCode((current) => (current !== "" && plans.plans.some((p) => p.code === current) ? current : plans.plans[0]?.code ?? ""));
    }
  }

  useEffect(() => {
    void load();
  }, [slug]);

  async function act(path: string, body: unknown | undefined, success: string, fallback: string) {
    setBusy(true);
    setNotice(null);
    const response: BffResponse | null = await bffRequest(path, {
      method: "POST",
      body,
    });
    setBusy(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      setNotice({ kind: "done", text: success, detail: null });
      await load();
      return response;
    }
    const failure = billingFeedback(response, fallback);
    setNotice({ kind: "error", text: failure.text, detail: failure.detail });
    return null;
  }

  async function checkout() {
    if (planCode === "") return;
    const response = await act(
      "/api/proxy/api/v1/subscriptions/checkout",
      { schoolSlug: slug, planCode },
      "Płatność została przygotowana.",
      "Nie udało się przygotować płatności.",
    );
    if (response !== null && isCheckout(response.body)) {
      setPending({ subscriptionId: response.body.subscriptionId, paymentUrl: safeHttpUrl(response.body.paymentUrl) });
    }
  }

  if (failed !== null) {
    return (
      <p className="notice" role="alert">
        {failed}
      </p>
    );
  }
  if (loaded === null) return <p className="muted">Ładowanie…</p>;

  const claim = pickClaim(loaded.claims);
  const subscription = claim?.subscription ?? null;
  const mock = loaded.plans?.mode === "mock";
  const profileWritable = subscription !== null && (subscription.status === "active" || subscription.status === "canceling");
  const checkoutId = pending?.subscriptionId ?? subscription?.id ?? null;

  return (
    <div>
      {notice !== null ? (
        <div role={notice.kind === "error" ? "alert" : "status"} className="notice">
          <p>{notice.text}</p>
          {notice.detail !== null ? <p className="muted">{notice.detail}</p> : null}
        </div>
      ) : null}

      <section aria-labelledby="status-heading">
        <h2 id="status-heading">Stan zgłoszenia</h2>
        {claim === null ? (
          <p>
            Nie zgłosiłeś jeszcze reprezentacji tej szkoły. Zrób to na <Link href="/konto/szkola">stronie konta szkoły</Link>.
          </p>
        ) : (
          <p>
            Zgłoszenie: <strong>{CLAIM_STATUS_LABEL[claim.status] ?? claim.status}</strong> (rola: {claim.roleDescription})
          </p>
        )}
        <p className="school-actions">
          <Link href={`/szkoly/${encodeURIComponent(slug)}`}>Zobacz publiczny profil szkoły</Link>
        </p>
      </section>

      {claim !== null && claim.status === "approved" ? (
        <>
          <section aria-labelledby="plan-heading">
            <h2 id="plan-heading">Plan i płatność</h2>
            {subscription !== null ? (
              <div className="card">
                <p>
                  Subskrypcja: <strong>{SUBSCRIPTION_STATUS_LABEL[subscription.status] ?? subscription.status}</strong>
                  {subscription.planCode ? ` · plan ${subscription.planCode}` : ""}
                </p>
                {formatDatePl(subscription.currentPeriodEnd) ? (
                  <p className="muted">Okres opłacony do: {formatDatePl(subscription.currentPeriodEnd)}</p>
                ) : null}
                {subscription.status === "active" && !subscription.cancelAtPeriodEnd ? (
                  <p>
                    <button
                      type="button"
                      className="button"
                      disabled={busy}
                      onClick={() =>
                        act(
                          `/api/proxy/api/v1/subscriptions/${encodeURIComponent(subscription.id)}/cancel`,
                          undefined,
                          "Subskrypcja zakończy się po opłaconym okresie. Nic nie zostanie usunięte.",
                          "Nie udało się zmienić subskrypcji.",
                        )
                      }
                    >
                      Anuluj od końca okresu
                    </button>
                  </p>
                ) : null}
                {subscription.status === "canceling" || subscription.cancelAtPeriodEnd ? (
                  <p>
                    <button
                      type="button"
                      className="button"
                      disabled={busy}
                      onClick={() =>
                        act(
                          `/api/proxy/api/v1/subscriptions/${encodeURIComponent(subscription.id)}/resume`,
                          undefined,
                          "Subskrypcja będzie odnawiana.",
                          "Nie udało się wznowić subskrypcji.",
                        )
                      }
                    >
                      Wznów subskrypcję
                    </button>
                  </p>
                ) : null}
              </div>
            ) : null}

            {subscription === null || subscription.status === "expired" ? (
              <CheckoutBlock
                plans={loaded.plans}
                planCode={planCode}
                planId={planId}
                onPlan={setPlanCode}
                onCheckout={checkout}
                busy={busy}
              />
            ) : null}

            {checkoutId !== null && (pending !== null || subscription?.status === "pending") ? (
              <div className="card">
                <p>Dokończ płatność u dostawcy. Po jej potwierdzeniu profil zostanie odblokowany.</p>
                {pending?.paymentUrl ? (
                  <p>
                    <a href={pending.paymentUrl} rel="nofollow noopener noreferrer">
                      Przejdź do płatności
                    </a>
                  </p>
                ) : null}
                {mock ? (
                  <p>
                    <button
                      type="button"
                      className="button"
                      disabled={busy}
                      onClick={() =>
                        act(
                          "/api/proxy/api/v1/billing/simulate",
                          { subscriptionId: checkoutId, event: "payment_succeeded" },
                          "Płatność testowa została potwierdzona.",
                          "Nie udało się potwierdzić płatności testowej.",
                        )
                      }
                    >
                      Symuluj opłacenie (tryb testowy)
                    </button>
                  </p>
                ) : null}
              </div>
            ) : null}
          </section>

          {profileWritable ? (
            <section aria-labelledby="profile-heading">
              <ProfileEditor
                key={`${slug}-${loaded.premium?.profile?.headline ?? ""}`}
                slug={slug}
                initial={loaded.premium?.profile ?? null}
                onSaved={() => void load()}
              />
            </section>
          ) : (
            <p className="muted">
              Edycja profilu jest dostępna przy aktywnej subskrypcji. Dane profilu pozostają zachowane.
            </p>
          )}

          <EnquiryInbox slug={slug} />
        </>
      ) : null}
    </div>
  );
}

function CheckoutBlock({
  plans,
  planCode,
  planId,
  onPlan,
  onCheckout,
  busy,
}: {
  readonly plans: PlansEnvelope | null;
  readonly planCode: string;
  readonly planId: string;
  readonly onPlan: (code: string) => void;
  readonly onCheckout: () => void;
  readonly busy: boolean;
}) {
  if (plans === null) {
    return <p className="muted">Płatne profile są chwilowo niedostępne.</p>;
  }
  if (plans.plans.length === 0) {
    return <p className="muted">{plans.note ?? "Cennik w przygotowaniu."}</p>;
  }
  return (
    <div className="card">
      <div className="field">
        <label htmlFor={planId}>Wybierz plan</label>
        <select id={planId} value={planCode} onChange={(event) => onPlan(event.target.value)}>
          {plans.plans.map((plan) => (
            <option key={plan.code} value={plan.code}>
              {plan.name}
              {plan.priceGrossPln !== null
                ? ` · ${formatDecimal(plan.priceGrossPln)} zł brutto / ${INTERVAL_LABEL[plan.interval] ?? plan.interval}`
                : " · cena w przygotowaniu"}
            </option>
          ))}
        </select>
      </div>
      <button type="button" className="button" onClick={onCheckout} disabled={busy || planCode === ""}>
        Przejdź do płatności
      </button>
    </div>
  );
}

function pickClaim(claims: readonly MyClaimView[]): MyClaimView | null {
  const rank: Record<string, number> = { approved: 0, pending: 1, rejected: 2, revoked: 3 };
  const sorted = [...claims].sort((a, b) => (rank[a.status] ?? 9) - (rank[b.status] ?? 9));
  return sorted[0] ?? null;
}

function isCheckout(body: unknown): body is { subscriptionId: string; paymentUrl: string | null } {
  return (
    typeof body === "object" &&
    body !== null &&
    typeof (body as { subscriptionId?: unknown }).subscriptionId === "string"
  );
}
