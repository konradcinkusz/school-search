"use client";

import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorListOf, errorMessageOf, isUnavailable } from "@/lib/bff-client";
import type { NoticeCreated } from "@/lib/reviews-types";

/**
 * Notice of illegal content (DSA art. 16). Anonymous by design: no account is needed.
 * The acknowledgement token is shown once and is the only way to check the status later.
 */
export const NOTICE_CATEGORIES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "illegal_content", label: "Treść niezgodna z prawem" },
  { value: "personal_data", label: "Dane osobowe (np. imiona dzieci lub nauczycieli)" },
  { value: "defamation", label: "Zniesławienie lub obraza" },
  { value: "other", label: "Inne" },
];

const EXPLANATION_MIN = 20;
const EXPLANATION_MAX = 3000;

export function NoticeForm({
  initialReference,
  reviewId,
}: {
  readonly initialReference: string;
  readonly reviewId: string | undefined;
}) {
  const ids = {
    reference: useId(),
    category: useId(),
    explanation: useId(),
    name: useId(),
    email: useId(),
    good: useId(),
  };
  const [sending, setSending] = useState(false);
  const [created, setCreated] = useState<NoticeCreated | null>(null);
  const [error, setError] = useState<{ text: string; details: string[] } | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const contentReference = String(data.get("contentReference") ?? "").trim();
    const category = String(data.get("category") ?? "");
    const explanation = String(data.get("explanation") ?? "").trim();
    const submitterName = String(data.get("submitterName") ?? "").trim();
    const submitterEmail = String(data.get("submitterEmail") ?? "").trim();
    const goodFaith = data.get("goodFaith") === "on";

    if (contentReference === "" || contentReference.length > 500) {
      setError({ text: "Podaj adres treści albo jej identyfikator (do 500 znaków).", details: [] });
      return;
    }
    if (explanation.length < EXPLANATION_MIN || explanation.length > EXPLANATION_MAX) {
      setError({
        text: `Uzasadnij zgłoszenie (od ${EXPLANATION_MIN} do ${EXPLANATION_MAX} znaków).`,
        details: [],
      });
      return;
    }
    if (!goodFaith) {
      setError({ text: "Potwierdź, że zgłoszenie składasz w dobrej wierze.", details: [] });
      return;
    }

    const body: Record<string, unknown> = {
      contentReference,
      category,
      explanation,
      goodFaithConfirmed: true,
    };
    if (submitterName !== "") body.submitterName = submitterName;
    if (submitterEmail !== "") body.submitterEmail = submitterEmail;
    if (reviewId !== undefined) body.reviewId = reviewId;

    setSending(true);
    setError(null);
    const response = await bffRequest("/api/proxy/api/v1/notices", { method: "POST", body });
    setSending(false);

    if (response !== null && response.status === 201 && isNoticeCreated(response.body)) {
      setCreated(response.body);
      return;
    }
    if (response === null || isUnavailable(response)) {
      setError({ text: "Usługa zgłoszeń jest chwilowo niedostępna. Zgłoszenie nie zostało wysłane.", details: [] });
      return;
    }
    if (response.status === 429) {
      setError({ text: "Wysłano zbyt wiele zgłoszeń. Spróbuj ponownie później.", details: [] });
      return;
    }
    setError({
      text: errorMessageOf(response.body) ?? "Zgłoszenie nie zostało przyjęte. Sprawdź dane i spróbuj ponownie.",
      details: errorListOf(response.body),
    });
  }

  if (created !== null) {
    return (
      <div role="status" className="notice">
        <p>
          <strong>Zgłoszenie przyjęte.</strong> Numer zgłoszenia: <span className="token">{created.id}</span>
        </p>
        <p>
          Token potwierdzenia: <span className="token">{created.ackToken}</span>
        </p>
        <p>
          Zapisz numer i token. Są potrzebne, aby sprawdzić status zgłoszenia i ewentualnie złożyć odwołanie. Pokażemy
          Ci je tylko raz.
        </p>
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={ids.reference}>Adres treści lub jej identyfikator</label>
        <input id={ids.reference} name="contentReference" type="text" maxLength={500} defaultValue={initialReference} required />
      </div>
      <div className="field">
        <label htmlFor={ids.category}>Czego dotyczy zgłoszenie?</label>
        <select id={ids.category} name="category" defaultValue="illegal_content">
          {NOTICE_CATEGORIES.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.explanation}>Dlaczego treść jest niezgodna z prawem lub regulaminem?</label>
        <textarea id={ids.explanation} name="explanation" rows={6} minLength={EXPLANATION_MIN} maxLength={EXPLANATION_MAX} required />
      </div>
      <div className="field">
        <label htmlFor={ids.name}>Imię lub pseudonim (opcjonalnie)</label>
        <input id={ids.name} name="submitterName" type="text" maxLength={120} autoComplete="name" />
      </div>
      <div className="field">
        <label htmlFor={ids.email}>Adres e-mail do informacji o decyzji (opcjonalnie)</label>
        <input id={ids.email} name="submitterEmail" type="email" maxLength={200} autoComplete="email" />
      </div>
      <div className="field field--check">
        <input id={ids.good} name="goodFaith" type="checkbox" required />
        <label htmlFor={ids.good}>Potwierdzam, że zgłoszenie składam w dobrej wierze, a podane informacje są zgodne z moją wiedzą.</label>
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Wysyłanie…" : "Wyślij zgłoszenie"}
      </button>
      {error !== null ? (
        <div role="alert" className="notice">
          <p>{error.text}</p>
          {error.details.length > 0 ? (
            <ul>
              {error.details.map((detail) => (
                <li key={detail}>{detail}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}

function isNoticeCreated(value: unknown): value is NoticeCreated {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.id === "string" && typeof v.ackToken === "string" && typeof v.receivedAt === "string";
}
