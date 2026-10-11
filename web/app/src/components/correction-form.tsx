"use client";

import Link from "next/link";
import { useId, useState, type FormEvent } from "react";

/**
 * Correction intake. Posts through the BFF proxy (/api/proxy/api/v1/corrections), never to the catalog directly.
 * The browser never sees a backend address. API-fed text is rendered as text only.
 */
const FIELD_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "name", label: "Nazwa szkoły" },
  { value: "address", label: "Adres" },
  { value: "governing_body", label: "Organ prowadzący" },
  { value: "cohort", label: "Uczniowie i roczniki" },
  { value: "score", label: "Wynik lub obliczenia" },
  { value: "other", label: "Inne" },
];

const MESSAGE_MAX = 2000;
const EMAIL_MAX = 200;
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

type Submitted = { token: string; status: string };

type Feedback =
  | { kind: "idle" }
  | { kind: "error"; text: string }
  | { kind: "done"; submitted: Submitted };

function errorText(status: number, body: unknown): string {
  if (status === 429) return "Wysłano zbyt wiele zgłoszeń. Spróbuj ponownie za chwilę.";
  if (status === 503) return "Usługa jest chwilowo niedostępna. Zgłoszenie nie zostało wysłane. Spróbuj ponownie.";
  if (typeof body === "object" && body !== null && "error" in body) {
    const error = (body as { error?: { message?: unknown } }).error;
    if (error && typeof error.message === "string" && error.message.length < 300) {
      return `Zgłoszenie nie zostało przyjęte: ${error.message}`;
    }
  }
  return "Zgłoszenie nie zostało przyjęte. Sprawdź dane i spróbuj ponownie.";
}

export function CorrectionForm({ initialSlug }: { initialSlug: string | undefined }) {
  const ids = {
    slug: useId(),
    field: useId(),
    message: useId(),
    email: useId(),
  };
  const [sending, setSending] = useState(false);
  const [feedback, setFeedback] = useState<Feedback>({ kind: "idle" });

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const slug = String(data.get("slug") ?? "").trim();
    const field = String(data.get("field") ?? "").trim();
    const message = String(data.get("message") ?? "").trim();
    const email = String(data.get("contactEmail") ?? "").trim();

    if (!SLUG_PATTERN.test(slug) || slug.length > 120) {
      setFeedback({ kind: "error", text: "Podaj identyfikator szkoły (slug) z adresu jej profilu." });
      return;
    }
    if (message === "" || message.length > MESSAGE_MAX) {
      setFeedback({ kind: "error", text: `Opisz korektę (do ${MESSAGE_MAX} znaków).` });
      return;
    }
    if (email.length > EMAIL_MAX) {
      setFeedback({ kind: "error", text: "Adres e-mail jest za długi." });
      return;
    }

    const body: Record<string, string> = { slug, field, message };
    if (email !== "") body.contactEmail = email;

    setSending(true);
    setFeedback({ kind: "idle" });
    try {
      const response = await fetch("/api/proxy/api/v1/corrections", {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(body),
      });
      const payload: unknown = await response.json().catch(() => null);
      if (response.status === 201 && typeof payload === "object" && payload !== null) {
        const token = (payload as { token?: unknown }).token;
        const status = (payload as { status?: unknown }).status;
        if (typeof token === "string" && typeof status === "string") {
          setFeedback({ kind: "done", submitted: { token, status } });
          form.reset();
          return;
        }
      }
      setFeedback({ kind: "error", text: errorText(response.status, payload) });
    } catch {
      setFeedback({ kind: "error", text: "Nie udało się połączyć z usługą. Zgłoszenie nie zostało wysłane." });
    } finally {
      setSending(false);
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={ids.slug}>Identyfikator szkoły (slug)</label>
        <input
          id={ids.slug}
          name="slug"
          type="text"
          defaultValue={initialSlug ?? ""}
          maxLength={120}
          autoComplete="off"
          required
        />
      </div>
      <div className="field">
        <label htmlFor={ids.field}>Czego dotyczy korekta?</label>
        <select id={ids.field} name="field" defaultValue="other">
          {FIELD_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.message}>Co jest błędne i jakie są właściwe dane?</label>
        <textarea id={ids.message} name="message" rows={5} maxLength={MESSAGE_MAX} required />
      </div>
      <div className="field">
        <label htmlFor={ids.email}>Adres e-mail do kontaktu (opcjonalnie)</label>
        <input id={ids.email} name="contactEmail" type="email" autoComplete="email" maxLength={EMAIL_MAX} />
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Wysyłanie…" : "Wyślij zgłoszenie"}
      </button>

      {feedback.kind === "error" ? (
        <p role="alert" className="notice">
          {feedback.text}
        </p>
      ) : null}
      {feedback.kind === "done" ? (
        <div role="status" className="notice">
          <p>
            <strong>Zgłoszenie przyjęte.</strong> Status: nowe. Zachowaj poniższy token, aby sprawdzić status
            zgłoszenia.
          </p>
          <p>
            Token: <code className="token">{feedback.submitted.token}</code>
          </p>
          <p>
            <Link href={`/dla-szkol/zgloszenie/${encodeURIComponent(feedback.submitted.token)}`}>
              Sprawdź status zgłoszenia
            </Link>
          </p>
        </div>
      ) : null}
    </form>
  );
}
