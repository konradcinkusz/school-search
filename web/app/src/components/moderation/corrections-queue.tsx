"use client";

import { useEffect, useId, useState } from "react";
import { AI_LABEL, parseAdminCorrections, parseCorrectionTriage, type AdminCorrection, type CorrectionTriage } from "@/lib/ai-contract";
import { aiFailureText } from "@/lib/ai-messages";
import { bffRequest, type BffResponse } from "@/lib/bff-client";
import { formatDatePl } from "@/lib/format";

/**
 * Corrections for moderators (SPEC-G, feature 5). The AI button proposes a draft reply and a triage label. The draft is
 * a suggestion for copy-paste: nothing here sends e-mail, changes a status or publishes anything. The catalog re-checks
 * the operator role on every call.
 */

const STATUS_FILTERS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "new", label: "nowe" },
  { value: "in_review", label: "w trakcie" },
  { value: "accepted", label: "przyjęte" },
  { value: "rejected", label: "odrzucone" },
  { value: "duplicate", label: "duplikaty" },
];

const FIELD_LABEL: Record<string, string> = {
  name: "Nazwa szkoły",
  address: "Adres",
  governing_body: "Organ prowadzący",
  cohort: "Uczniowie i roczniki",
  score: "Wynik lub obliczenia",
  other: "Inne",
  tuition_min: "Czesne: minimum",
  tuition_max: "Czesne: maksimum",
  hours: "Godziny otwarcia",
  free_places: "Wolne miejsca",
};

type Load = "loading" | "ready" | "failed";

export function CorrectionsQueueView() {
  const filterId = useId();
  const [status, setStatus] = useState("new");
  const [load, setLoad] = useState<Load>("loading");
  const [loadText, setLoadText] = useState<string>("");
  const [items, setItems] = useState<AdminCorrection[]>([]);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    (async () => {
      setLoad("loading");
      const response = await bffRequest(`/api/proxy/api/v1/admin/corrections?status=${encodeURIComponent(status)}&pageSize=50`);
      if (!active) return;
      const parsed = response?.status === 200 ? parseAdminCorrections(response.body) : null;
      if (parsed === null) {
        setLoadText(loadFailureText(response));
        setLoad("failed");
        return;
      }
      setItems([...parsed.items]);
      setLoad("ready");
    })();
    return () => {
      active = false;
    };
  }, [status, reloadKey]);

  return (
    <div>
      <div className="field">
        <label htmlFor={filterId}>Status korekty</label>
        <select id={filterId} value={status} onChange={(event) => setStatus(event.target.value)}>
          {STATUS_FILTERS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      {load === "loading" ? <p className="muted">Ładowanie korekt…</p> : null}
      {load === "failed" ? (
        <p className="notice" role="alert">
          {loadText}
        </p>
      ) : null}
      {load === "ready" && items.length === 0 ? <p className="muted">Brak korekt w tym statusie.</p> : null}
      {load === "ready" && items.length > 0 ? (
        <ul className="plain-list">
          {items.map((item) => (
            <CorrectionRow key={item.id} item={item} onChanged={() => setReloadKey((k) => k + 1)} />
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function loadFailureText(response: BffResponse | null): string {
  if (response?.status === 401) return "Zaloguj się, aby zobaczyć korekty.";
  if (response?.status === 403) return "Ta strona jest dostępna wyłącznie dla moderatorów.";
  if (response?.status === 404) return "Kolejka korekt nie jest włączona w tym środowisku.";
  return "Kolejka korekt jest chwilowo niedostępna.";
}

function CorrectionRow({ item, onChanged }: { readonly item: AdminCorrection; readonly onChanged: () => void }) {
  const draftId = useId();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [triage, setTriage] = useState<CorrectionTriage | null>(null);
  const created = formatDatePl(item.createdAt);

  async function proposeReply() {
    setBusy(true);
    setFailure(null);
    const response = await bffRequest(`/api/proxy/api/v1/admin/corrections/${encodeURIComponent(item.id)}/triage`, {
      method: "POST",
    });
    setBusy(false);
    const parsed = response?.status === 200 ? parseCorrectionTriage(response.body) : null;
    if (parsed === null) {
      setTriage(null);
      setFailure(aiFailureText(response));
      return;
    }
    setTriage(parsed);
  }

  async function copyDraft(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      setFailure(null);
    } catch {
      setFailure("Nie udało się skopiować. Zaznacz tekst i skopiuj go ręcznie.");
    }
  }

  return (
    <li className="card">
      <p className="muted">
        {item.schoolSlug ? `szkoła: ${item.schoolSlug}` : "szkoła nieznana"} · {FIELD_LABEL[item.field] ?? item.field}
        {created ? ` · ${created}` : ""} · status: {item.status}
      </p>
      <p className="review__body">{item.message}</p>
      {item.contactEmail ? (
        <p className="muted">
          Kontakt zgłaszającego (poza systemem): <span className="token">{item.contactEmail}</span>
        </p>
      ) : null}
      <p className="muted">
        Identyfikator: <span className="token">{item.id}</span>
      </p>

      <button type="button" className="button" onClick={proposeReply} disabled={busy}>
        {busy ? "Przygotowywanie propozycji…" : "Zaproponuj odpowiedź (AI)"}
      </button>

      {failure !== null ? (
        <p className="notice" role="alert">
          {failure}
        </p>
      ) : null}

      {triage !== null ? (
        <section className="ai-box" aria-label="Propozycja AI">
          <p className="ai-box__label">{triage.aiLabel || AI_LABEL}</p>
          <p className="muted">
            Kategoria: {triage.category || "brak"}
            {triage.severity ? ` · pilność: ${triage.severity}` : ""}
            {triage.fieldLooksValid === null ? "" : ` · pole wygląda ${triage.fieldLooksValid ? "poprawnie" : "podejrzanie"}`}
          </p>
          {triage.suggestedAction ? <p className="muted">Sugerowany krok: {triage.suggestedAction}</p> : null}
          <label htmlFor={draftId}>Propozycja odpowiedzi (tylko do skopiowania, nic nie zostanie wysłane)</label>
          <textarea id={draftId} rows={6} readOnly value={triage.draftReplyPl} className="ai-draft" />
          <p>
            <button type="button" className="link-button" onClick={() => copyDraft(triage.draftReplyPl)}>
              Skopiuj propozycję
            </button>
          </p>
          <p className="muted">
            Sprawdź treść przed użyciem. Decyzję o korekcie podejmuje człowiek.{" "}
            <button type="button" className="link-button" onClick={onChanged}>
              Odśwież kolejkę
            </button>
          </p>
        </section>
      ) : null}
    </li>
  );
}
