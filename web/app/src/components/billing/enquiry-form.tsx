"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, isUnavailable } from "@/lib/bff-client";
import { ENQUIRY_EMAIL_MAX, ENQUIRY_MESSAGE_MAX } from "@/lib/billing-types";

/**
 * Parent enquiry to a school. Personal data (RODO): explicit consent with its version, minimal fields, nothing is
 * shown publicly and the message goes only to the school owner's dashboard. The honeypot field stays empty for people.
 */
type FieldKey = "email" | "message" | "consent";
type FieldErrors = Partial<Record<FieldKey, string>>;
const FIELD_ORDER: readonly FieldKey[] = ["email", "message", "consent"];
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function EnquiryForm({
  slug,
  consentVersion,
  consentText,
}: {
  readonly slug: string;
  readonly consentVersion: string;
  readonly consentText: string;
}) {
  const ids = {
    email: useId(),
    emailError: useId(),
    message: useId(),
    messageError: useId(),
    consent: useId(),
    consentError: useId(),
  };
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState(false);
  const [fields, setFields] = useState<FieldErrors>({});
  const [summary, setSummary] = useState<string | null>(null);

  const idOf: Record<FieldKey, string> = { email: ids.email, message: ids.message, consent: ids.consent };

  /** Shows the errors and moves focus to the first invalid field, in form order. */
  function reject(nextFields: FieldErrors, text: string | null) {
    setFields(nextFields);
    setSummary(text);
    const first = FIELD_ORDER.find((key) => nextFields[key] !== undefined);
    if (first !== undefined) document.getElementById(idOf[first])?.focus();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email") ?? "").trim();
    const message = String(data.get("message") ?? "").trim();
    const website = String(data.get("website") ?? "");
    const accepted = data.get("consent") === "on";

    const clientFields: FieldErrors = {};
    if (email === "" || email.length > ENQUIRY_EMAIL_MAX || !EMAIL_SHAPE.test(email)) {
      clientFields.email = "Podaj poprawny adres e-mail.";
    }
    if (message === "" || message.length > ENQUIRY_MESSAGE_MAX) {
      clientFields.message = `Napisz wiadomość (do ${ENQUIRY_MESSAGE_MAX} znaków).`;
    }
    if (!accepted) clientFields.consent = "Potwierdź zgodę, aby wysłać wiadomość.";
    if (Object.keys(clientFields).length > 0) {
      reject(clientFields, "Popraw pola oznaczone błędem i wyślij wiadomość ponownie.");
      return;
    }

    setSending(true);
    setFields({});
    setSummary(null);
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
      setSummary("Usługa kontaktu jest chwilowo niedostępna. Wiadomość nie została wysłana.");
      return;
    }
    if (response.status === 404) {
      setSummary("Ta szkoła nie przyjmuje wiadomości przez formularz.");
      return;
    }
    if (response.status === 429) {
      setSummary("Wysłano zbyt wiele wiadomości. Spróbuj ponownie później.");
      return;
    }
    setSummary("Wiadomość nie została przyjęta. Sprawdź dane i spróbuj ponownie.");
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
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.email}>
          Twój adres e-mail<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.email}
          name="email"
          type="email"
          maxLength={ENQUIRY_EMAIL_MAX}
          autoComplete="email"
          required
          aria-invalid={fields.email ? true : undefined}
          aria-describedby={fields.email ? ids.emailError : undefined}
        />
        {fields.email ? (
          <p id={ids.emailError} className="field-error">
            {fields.email}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.message}>
          Wiadomość (do {ENQUIRY_MESSAGE_MAX} znaków)<span aria-hidden="true"> *</span>
        </label>
        <textarea
          id={ids.message}
          name="message"
          rows={5}
          maxLength={ENQUIRY_MESSAGE_MAX}
          required
          aria-invalid={fields.message ? true : undefined}
          aria-describedby={fields.message ? ids.messageError : undefined}
        />
        {fields.message ? (
          <p id={ids.messageError} className="field-error">
            {fields.message}
          </p>
        ) : null}
      </div>
      {/* Honeypot: hidden from people and assistive technology; bots that fill it are dropped by the service. */}
      <div className="hp-field" aria-hidden="true">
        <label htmlFor={`${ids.email}-website`}>Strona (zostaw puste)</label>
        <input id={`${ids.email}-website`} name="website" type="text" tabIndex={-1} autoComplete="off" />
      </div>
      <div className="field field--check">
        <input
          id={ids.consent}
          name="consent"
          type="checkbox"
          required
          aria-invalid={fields.consent ? true : undefined}
          aria-describedby={fields.consent ? ids.consentError : undefined}
        />
        <label htmlFor={ids.consent}>
          {consentText}
          <span aria-hidden="true"> *</span>
        </label>
        {fields.consent ? (
          <p id={ids.consentError} className="field-error">
            {fields.consent}
          </p>
        ) : null}
      </div>
      <p className="muted">Wersja zgody: {consentVersion}</p>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Wysyłanie…" : "Wyślij wiadomość"}
      </button>
      {summary !== null ? (
        <p role="alert" className="notice">
          {summary}
        </p>
      ) : null}
    </form>
  );
}
