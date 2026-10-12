"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useEffect, useId, useState, type FormEvent } from "react";
import { bffRequest, isUnavailable, type BffResponse } from "@/lib/bff-client";
import { formatDatePl } from "@/lib/format";
import type { ModerationQueue, ModerationQueueItem } from "@/lib/reviews-types";

/**
 * Moderator queue (reviews, replies and notices). The Reviews service decides who may see it (role Admin or
 * SuperAdmin, re-checked on every call). The AI triage label only orders the queue: it is advisory and never a decision.
 */

const KIND_LABEL: Record<string, string> = {
  review: "Opinia",
  reply: "Odpowiedź szkoły",
  notice: "Zgłoszenie treści",
};

/** Path segment of the decision endpoint for each queue kind. */
const KIND_PATH: Record<string, string> = {
  review: "reviews",
  reply: "replies",
  notice: "notices",
};

const ACTIONS: Record<string, ReadonlyArray<{ value: string; label: string }>> = {
  review: [
    { value: "publish", label: "Opublikuj" },
    { value: "reject", label: "Odrzuć" },
    { value: "remove", label: "Usuń" },
  ],
  reply: [
    { value: "publish", label: "Opublikuj odpowiedź" },
    { value: "reject", label: "Odrzuć odpowiedź" },
  ],
  notice: [
    { value: "keep", label: "Pozostaw treść" },
    { value: "remove", label: "Usuń treść" },
  ],
};

const TRIAGE_LABEL: Record<string, string> = {
  ok: "bez uwag",
  needs_attention: "wymaga uwagi",
  likely_personal_data: "możliwe dane osobowe",
  likely_abusive: "możliwa obraza",
  unclear: "niejasne",
};

type Load = "loading" | "ready" | "forbidden" | "unauthenticated" | "unavailable";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function ModerationQueueView() {
  const [load, setLoad] = useState<Load>("loading");
  const [items, setItems] = useState<ModerationQueueItem[]>([]);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest("/api/proxy/api/v1/moderation/queue");
      if (!active) return;
      if (response === null || isUnavailable(response)) {
        setLoad("unavailable");
        return;
      }
      if (response.status === 401) {
        setLoad("unauthenticated");
        return;
      }
      if (response.status === 403) {
        setLoad("forbidden");
        return;
      }
      const body = response.body;
      if (response.status !== 200 || !isRecord(body) || !Array.isArray(body.items)) {
        setLoad("unavailable");
        return;
      }
      setItems(body.items.filter(isRecord).map((item) => item as unknown as ModerationQueueItem));
      setLoad("ready");
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  if (load === "loading") return <p className="muted">Ładowanie kolejki…</p>;
  if (load === "unauthenticated") {
    return (
      <p className="notice">
        <Link href="/konto/logowanie">Zaloguj się</Link>, aby zobaczyć kolejkę.
      </p>
    );
  }
  if (load === "forbidden") {
    return (
      <p className="notice" role="alert">
        Ta strona jest dostępna wyłącznie dla moderatorów.
      </p>
    );
  }
  if (load === "unavailable") {
    return (
      <p className="notice" role="alert">
        Kolejka moderacji jest chwilowo niedostępna.
      </p>
    );
  }
  if (items.length === 0) return <p className="muted">Kolejka jest pusta.</p>;

  return (
    <ul className="plain-list">
      {items.map((item) => (
        <QueueRow key={`${item.kind}-${item.id}`} item={item} onDone={() => setReloadKey((k) => k + 1)} />
      ))}
    </ul>
  );
}

function QueueRow({ item, onDone }: { readonly item: ModerationQueueItem; readonly onDone: () => void }) {
  const created = formatDatePl(item.createdAt);
  const triage = item.triage;
  const title = item.kind === "reply" ? item.reviewTitle : item.title;
  const body = item.kind === "reply" ? item.reviewBody : item.body;

  return (
    <li className="card">
      <p className="muted">
        {KIND_LABEL[item.kind] ?? item.kind}
        {item.schoolSlug ? ` · szkoła: ${item.schoolSlug}` : ""}
        {created ? ` · ${created}` : ""}
        {item.rating !== null ? ` · ocena osoby piszącej: ${item.rating} z 5` : ""}
      </p>
      {triage ? (
        <p className="muted">
          Podpowiedź AI, tylko orientacyjnie: {TRIAGE_LABEL[triage.label] ?? triage.label}
        </p>
      ) : null}
      <p className="muted">
        Identyfikator: <span className="token">{item.id}</span>
      </p>
      {title ? <h3 className="review__title">{title}</h3> : null}
      {body ? <p className="review__body">{body}</p> : null}
      {item.kind === "reply" && item.body ? (
        <p className="review__body">
          <span className="muted">Odpowiedź ({item.representativeLabel ?? "szkoła"}): </span>
          {item.body}
        </p>
      ) : null}
      {item.kind === "notice" ? (
        <>
          <p>
            Kategoria: {item.category ?? "brak"} · adres treści: <span className="token">{item.contentReference}</span>
          </p>
          {item.explanation ? <p className="review__body">{item.explanation}</p> : null}
        </>
      ) : null}
      <DecisionForm item={item} onDone={onDone} />
    </li>
  );
}

const DECISION_COPY = {
  ground: "Podaj podstawę decyzji (do 300 znaków).",
  facts: "Podaj okoliczności (do 2000 znaków).",
  forbidden: "Nie masz uprawnień do tej decyzji. Sprawdź rolę konta.",
  notFound: "Nie znaleziono tej pozycji. Odśwież kolejkę i spróbuj ponownie.",
  invalid: "Sprawdź podstawę i okoliczności decyzji, a potem zapisz ją ponownie.",
  unavailable: "Usługa moderacji jest chwilowo niedostępna. Decyzja nie została zapisana.",
  generic: "Nie udało się zapisać decyzji. Spróbuj ponownie.",
};

function decisionFailureText(response: BffResponse | null): string {
  if (response === null || isUnavailable(response)) return DECISION_COPY.unavailable;
  if (response.status === 401 || response.status === 403) return DECISION_COPY.forbidden;
  if (response.status === 404) return DECISION_COPY.notFound;
  if (response.status === 400) return DECISION_COPY.invalid;
  return DECISION_COPY.generic;
}

function DecisionForm({ item, onDone }: { readonly item: ModerationQueueItem; readonly onDone: () => void }) {
  const ids = {
    action: useId(),
    ground: useId(),
    groundError: useId(),
    facts: useId(),
    factsError: useId(),
  };
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<{ ground?: string; facts?: string }>({});
  const actions = ACTIONS[item.kind] ?? [];
  const path = KIND_PATH[item.kind];

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending || path === undefined) return;
    const data = new FormData(event.currentTarget);
    const action = String(data.get("action") ?? "");
    const ground = String(data.get("ground") ?? "").trim();
    const facts = String(data.get("facts") ?? "").trim();
    const invalid: { ground?: string; facts?: string } = {};
    if (ground === "" || ground.length > 300) invalid.ground = DECISION_COPY.ground;
    if (facts === "" || facts.length > 2000) invalid.facts = DECISION_COPY.facts;
    if (invalid.ground !== undefined || invalid.facts !== undefined) {
      setFields(invalid);
      setError(null);
      document.getElementById(invalid.ground !== undefined ? ids.ground : ids.facts)?.focus();
      return;
    }
    setSending(true);
    setFields({});
    setError(null);
    // Every decision is recorded with its statement of reasons; the service never decides automatically.
    const response = await bffRequest(`/api/proxy/api/v1/moderation/${path}/${encodeURIComponent(item.id)}/decision`, {
      method: "POST",
      body: { action, legalOrTermsGround: ground, facts },
    });
    setSending(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      onDone();
      return;
    }
    setError(decisionFailureText(response));
  }

  return (
    <details>
      <summary>Podejmij decyzję</summary>
      <form onSubmit={onSubmit} noValidate>
        <p className="field-hint">Pola oznaczone * są wymagane.</p>
        <div className="field">
          <label htmlFor={ids.action}>Decyzja</label>
          <select id={ids.action} name="action" defaultValue={actions[0]?.value}>
            {actions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor={ids.ground}>
            Podstawa prawna lub regulaminowa<span aria-hidden="true"> *</span>
          </label>
          <input
            id={ids.ground}
            name="ground"
            type="text"
            maxLength={300}
            required
            aria-invalid={fields.ground ? true : undefined}
            aria-describedby={fields.ground ? ids.groundError : undefined}
          />
          {fields.ground ? (
            <p id={ids.groundError} className="field-error">
              {fields.ground}
            </p>
          ) : null}
        </div>
        <div className="field">
          <label htmlFor={ids.facts}>
            Okoliczności faktyczne (trafią do uzasadnienia dla zgłaszającego i autora)<span aria-hidden="true"> *</span>
          </label>
          <textarea
            id={ids.facts}
            name="facts"
            rows={4}
            maxLength={2000}
            required
            aria-invalid={fields.facts ? true : undefined}
            aria-describedby={fields.facts ? ids.factsError : undefined}
          />
          {fields.facts ? (
            <p id={ids.factsError} className="field-error">
              {fields.facts}
            </p>
          ) : null}
        </div>
        <button type="submit" className="button" aria-disabled={sending || path === undefined ? true : undefined}>
          {sending ? "Zapisywanie…" : "Zapisz decyzję"}
        </button>
        {error !== null ? (
          <p role="alert" className="notice">
            {error}
          </p>
        ) : null}
      </form>
    </details>
  );
}
