"use client";

import Link from "next/link";
import { useEffect, useId, useState, type FormEvent } from "react";
import { billingFeedback, CLAIM_STATUS_LABEL } from "@/components/billing/billing-messages";
import { bffRequest, type BffResponse } from "@/lib/bff-client";
import { parseMyClaims } from "@/lib/billing-parse";
import { CLAIM_ROLE_MAX, type MyClaimView } from "@/lib/billing-types";
import { formatDatePl } from "@/lib/format";
import { isSlug } from "@/lib/validation";

/** "Konto szkoły": the claims of the signed-in user, and a new claim (self-declared role, checked by moderators). */
export function ClaimsOverview() {
  const ids = { slug: useId(), role: useId() };
  const [claims, setClaims] = useState<MyClaimView[] | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [feedback, setFeedback] = useState<{ kind: "error" | "done"; text: string; detail: string | null } | null>(null);

  async function load(): Promise<void> {
    const response = await bffRequest("/api/proxy/api/v1/claims/mine");
    const parsed = response?.status === 200 ? parseMyClaims(response.body) : null;
    if (parsed === null) {
      setFailed(billingFeedback(response, "Nie udało się pobrać zgłoszeń. Spróbuj ponownie.").text);
      return;
    }
    setFailed(null);
    setClaims(parsed);
  }

  useEffect(() => {
    void load();
  }, []);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const slug = String(data.get("schoolSlug") ?? "").trim();
    const roleDescription = String(data.get("roleDescription") ?? "").trim();
    if (!isSlug(slug)) {
      setFeedback({ kind: "error", text: "Podaj identyfikator szkoły z jej adresu (np. szkola-podstawowa-nr-1).", detail: null });
      return;
    }
    if (roleDescription === "" || roleDescription.length > CLAIM_ROLE_MAX) {
      setFeedback({ kind: "error", text: `Opisz swoją rolę (do ${CLAIM_ROLE_MAX} znaków).`, detail: null });
      return;
    }
    setSending(true);
    setFeedback(null);
    const response: BffResponse | null = await bffRequest(
      `/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/claims`,
      { method: "POST", body: { roleDescription } },
    );
    setSending(false);
    if (response !== null && response.status === 201) {
      setFeedback({
        kind: "done",
        text: "Zgłoszenie przyjęte. Redakcja sprawdzi je poza systemem. Do tego czasu profil nie jest publikowany.",
        detail: null,
      });
      await load();
      return;
    }
    const failure = billingFeedback(response, "Zgłoszenia nie udało się wysłać. Spróbuj ponownie.");
    setFeedback({ kind: "error", text: failure.text, detail: failure.detail });
  }

  return (
    <div>
      <p className="muted">
        Profil rozszerzony to płatna, oznaczona jako sponsorowana treść przygotowana przez przedstawiciela szkoły. Nie
        wpływa na wynik, ranking, kolejność ani opinie. Najpierw zgłoś swoją rolę. Redakcja sprawdza ją poza systemem.
      </p>

      <section aria-labelledby="claims-heading">
        <h2 id="claims-heading">Moje szkoły</h2>
        {failed !== null ? (
          <p className="notice" role="alert">
            {failed}
          </p>
        ) : claims === null ? (
          <p className="muted">Ładowanie…</p>
        ) : claims.length === 0 ? (
          <p className="muted">Nie zgłosiłeś jeszcze reprezentacji żadnej szkoły.</p>
        ) : (
          <ul className="plain-list">
            {claims.map((claim) => (
              <li key={claim.id} className="card">
                <h3>
                  <Link href={`/konto/szkola/${encodeURIComponent(claim.schoolSlug)}`}>{claim.schoolSlug}</Link>
                </h3>
                <p className="muted">
                  Rola: {claim.roleDescription} · {CLAIM_STATUS_LABEL[claim.status] ?? claim.status}
                  {formatDatePl(claim.createdAt) ? ` · zgłoszono ${formatDatePl(claim.createdAt)}` : ""}
                </p>
                {claim.subscription ? (
                  <p className="muted">Subskrypcja: {claim.subscription.planCode || "plan"}</p>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="new-claim-heading">
        <h2 id="new-claim-heading">Zgłoś reprezentację szkoły</h2>
        <form onSubmit={onSubmit} noValidate className="card">
          <div className="field">
            <label htmlFor={ids.slug}>Identyfikator szkoły (fragment adresu strony szkoły)</label>
            <input id={ids.slug} name="schoolSlug" type="text" maxLength={120} required />
          </div>
          <div className="field">
            <label htmlFor={ids.role}>Twoja rola w szkole (np. dyrekcja, sekretariat)</label>
            <input id={ids.role} name="roleDescription" type="text" maxLength={CLAIM_ROLE_MAX} required />
          </div>
          <button type="submit" className="button" disabled={sending}>
            {sending ? "Wysyłanie…" : "Wyślij zgłoszenie"}
          </button>
          {feedback !== null ? (
            <div role={feedback.kind === "error" ? "alert" : "status"} className="notice">
              <p>{feedback.text}</p>
              {feedback.detail !== null ? <p className="muted">{feedback.detail}</p> : null}
            </div>
          ) : null}
        </form>
      </section>
    </div>
  );
}
