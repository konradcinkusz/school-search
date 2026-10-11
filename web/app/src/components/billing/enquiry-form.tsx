"use client";

import { useId, useState, type FormEvent } from "react";
import { bffRequest, isUnavailable } from "@/lib/bff-client";
import { ENQUIRY_EMAIL_MAX, ENQUIRY_MESSAGE_MAX } from "@/lib/billing-types";

/**
 * Parent enquiry to a school. Personal data (RODO): explicit consent with its version, minimal fields, nothing is
 * shown publicly and the message goes only to the school owner's dashboard. The honeypot field stays empty for people.
 */
export function EnquiryForm({
  slug,
  consentVersion,
  consentText,
}: {
  readonly slug: string;
  readonly consentVersion: string;
  readonly consentText: string;
}) {
  const ids = { email: useId(), message: useId(), consent: useId() };
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email") ?? "").trim();
    const message = String(data.get("message") ?? "").trim();
    const website = String(data.get("website") ?? "");
    const accepted = data.get("consent") === "on";

    if (email === "" || email.length > ENQUIRY_EMAIL_MAX || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      setError("Podaj poprawny adres e-mail.");
      return;
    }
    if (message === "" || message.length > ENQUIRY_MESSAGE_MAX) {
      setError(`Napisz wiadomość (do ${ENQUIRY_MESSAGE_MAX} znaków).`);
      return;
    }
    if (!accepted) {
      setError("Potwierdź zgodę, aby wysłać wiadomość.");
      return;
    }

    setSending(true);
    setError(null);
    const response = await bffRequest(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/enquiries`, {
      method: "POST",
      body: { email, message, acceptedConsentVersion: consentVersion, website },
    });
    setSending(false);

    if (response !== null && response.status === 202) {
      setDone(true);
      return;
    }
    if (response === null || isUnavailable(response)) {
      setError("Usługa kontaktu jest chwilowo niedostępna. Wiadomość nie została wysłana.");
      return;
    }
    if (response.status === 404) {
      setError("Ta szkoła nie przyjmuje wiadomości przez formularz.");
      return;
    }
    if (response.status === 429) {
      setError("Wysłano zbyt wiele wiadomości. Spróbuj ponownie później.");
      return;
    }
    setError("Wiadomość nie została przyjęta. Sprawdź dane i spróbuj ponownie.");
  }

  if (done) {
    return (
      <div role="status" className="notice">
        <p>
          <strong>Wiadomość została wysłana do szkoły.</strong> Szkoła odpowie na adres, który podano.
        </p>
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} noValidate className="card enquiry-form">
      <h3>Napisz do szkoły</h3>
      <p className="muted">
        Wiadomość trafi wyłącznie do osoby prowadzącej profil szkoły. Nie jest publikowana ani nie jest wysyłana
        marketingowo.
      </p>
      <div className="field">
        <label htmlFor={ids.email}>Twój adres e-mail</label>
        <input id={ids.email} name="email" type="email" maxLength={ENQUIRY_EMAIL_MAX} autoComplete="email" required />
      </div>
      <div className="field">
        <label htmlFor={ids.message}>Wiadomość (do {ENQUIRY_MESSAGE_MAX} znaków)</label>
        <textarea id={ids.message} name="message" rows={5} maxLength={ENQUIRY_MESSAGE_MAX} required />
      </div>
      {/* Honeypot: hidden from people and assistive technology; bots that fill it are dropped by the service. */}
      <div className="hp-field" aria-hidden="true">
        <label htmlFor={`${ids.email}-website`}>Strona (zostaw puste)</label>
        <input id={`${ids.email}-website`} name="website" type="text" tabIndex={-1} autoComplete="off" />
      </div>
      <div className="field field--check">
        <input id={ids.consent} name="consent" type="checkbox" required />
        <label htmlFor={ids.consent}>{consentText}</label>
      </div>
      <p className="muted">Wersja zgody: {consentVersion}</p>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Wysyłanie…" : "Wyślij wiadomość"}
      </button>
      {error !== null ? (
        <p role="alert" className="notice">
          {error}
        </p>
      ) : null}
    </form>
  );
}
