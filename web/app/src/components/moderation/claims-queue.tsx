"use client";

import { useEffect, useId, useState, type FormEvent } from "react";
import { billingFeedback, CLAIM_STATUS_LABEL } from "@/components/billing/billing-messages";
import { bffRequest } from "@/lib/bff-client";
import { parseModerationClaims } from "@/lib/billing-parse";
import type { ModerationClaimView } from "@/lib/billing-types";
import { formatDatePl } from "@/lib/format";

/**
 * Moderator queue for school claims. Approving a claim is the ONLY way a sponsored profile becomes publishable, and
 * the check happens outside the system. Decisions are never automatic. The Billing service re-checks the role.
 */
const STATUS_FILTERS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "pending", label: "oczekujące" },
  { value: "approved", label: "zatwierdzone" },
  { value: "rejected", label: "odrzucone" },
  { value: "revoked", label: "cofnięte" },
];

const ACTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "approve", label: "Zatwierdź przedstawiciela" },
  { value: "reject", label: "Odrzuć zgłoszenie" },
  { value: "revoke", label: "Cofnij zatwierdzenie" },
];

export function ClaimsQueueView() {
  const filterId = useId();
  const [status, setStatus] = useState("pending");
  const [items, setItems] = useState<ModerationClaimView[] | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest(`/api/proxy/api/v1/moderation/claims?status=${encodeURIComponent(status)}`);
      if (!active) return;
      const parsed = response?.status === 200 ? parseModerationClaims(response.body) : null;
      if (parsed === null) {
        setItems(null);
        setFailed(billingFeedback(response, "Kolejka zgłoszeń właścicieli jest chwilowo niedostępna.").text);
        return;
      }
      setFailed(null);
      setItems(parsed);
    })();
    return () => {
      active = false;
    };
  }, [status, reloadKey]);

  return (
    <div>
      <div className="field">
        <label htmlFor={filterId}>Status zgłoszeń</label>
        <select id={filterId} value={status} onChange={(event) => setStatus(event.target.value)}>
          {STATUS_FILTERS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      {failed !== null ? (
        <p className="notice" role="alert">
          {failed}
        </p>
      ) : items === null ? (
        <p className="muted">Ładowanie kolejki…</p>
      ) : items.length === 0 ? (
        <p className="muted">Brak zgłoszeń w tym statusie.</p>
      ) : (
        <ul className="plain-list">
          {items.map((item) => (
            <ClaimRow key={item.id} item={item} onDone={() => setReloadKey((k) => k + 1)} />
          ))}
        </ul>
      )}
    </div>
  );
}

function ClaimRow({ item, onDone }: { readonly item: ModerationClaimView; readonly onDone: () => void }) {
  const ids = { action: useId(), notes: useId() };
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const action = String(data.get("action") ?? "approve");
    const notes = String(data.get("notes") ?? "").trim();
    if (notes.length > 500) {
      setError("Notatka może mieć do 500 znaków.");
      return;
    }
    setSending(true);
    setError(null);
    const response = await bffRequest(`/api/proxy/api/v1/moderation/claims/${encodeURIComponent(item.id)}/decision`, {
      method: "POST",
      body: { action, notes: notes === "" ? null : notes },
    });
    setSending(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      onDone();
      return;
    }
    setError(billingFeedback(response, "Nie udało się zapisać decyzji.").text);
  }

  return (
    <li className="card">
      <p className="muted">
        Szkoła: {item.schoolSlug} · {CLAIM_STATUS_LABEL[item.status] ?? item.status}
        {formatDatePl(item.createdAt) ? ` · zgłoszono ${formatDatePl(item.createdAt)}` : ""}
      </p>
      <p>
        Deklarowana rola: <strong>{item.roleDescription}</strong>
      </p>
      <p className="muted">
        Identyfikator: <span className="token">{item.id}</span>
      </p>
      <details>
        <summary>Podejmij decyzję</summary>
        <form onSubmit={onSubmit} noValidate>
          <div className="field">
            <label htmlFor={ids.action}>Decyzja</label>
            <select id={ids.action} name="action" defaultValue="approve">
              {ACTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={ids.notes}>Notatka wewnętrzna (do 500 znaków, nie jest pokazywana publicznie)</label>
            <textarea id={ids.notes} name="notes" rows={3} maxLength={500} />
          </div>
          <button type="submit" className="button" disabled={sending}>
            {sending ? "Zapisywanie…" : "Zapisz decyzję"}
          </button>
        </form>
      </details>
      {error !== null ? (
        <p role="alert" className="notice">
          {error}
        </p>
      ) : null}
    </li>
  );
}
