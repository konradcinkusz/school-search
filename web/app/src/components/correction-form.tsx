"use client";

import { useId, useState, type FormEvent } from "react";

/**
 * Stub: the correction request is validated locally and never sent anywhere.
 * The moderated intake (and its contact point) is delivered in a later phase.
 */
export function CorrectionForm() {
  const ids = {
    school: useId(),
    rspo: useId(),
    detail: useId(),
    contact: useId(),
  };
  const [status, setStatus] = useState<string | null>(null);

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const school = String(data.get("school") ?? "").trim();
    const detail = String(data.get("detail") ?? "").trim();
    if (school === "" || detail === "") {
      setStatus("Uzupełnij nazwę szkoły i opis korekty.");
      return;
    }
    setStatus(
      "Formularz jest w przygotowaniu. Twoje zgłoszenie nie zostało wysłane i nie zostało zapisane.",
    );
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={ids.school}>Nazwa szkoły</label>
        <input id={ids.school} name="school" type="text" required />
      </div>
      <div className="field">
        <label htmlFor={ids.rspo}>Numer RSPO (opcjonalnie)</label>
        <input id={ids.rspo} name="rspo" type="text" inputMode="numeric" />
      </div>
      <div className="field">
        <label htmlFor={ids.detail}>Co jest błędne i jakie są właściwe dane?</label>
        <textarea id={ids.detail} name="detail" rows={5} required />
      </div>
      <div className="field">
        <label htmlFor={ids.contact}>Adres e-mail do kontaktu (opcjonalnie)</label>
        <input id={ids.contact} name="contact" type="email" autoComplete="email" />
      </div>
      {status ? (
        <p role="status" className="notice">
          {status}
        </p>
      ) : null}
      <button type="submit" className="button">
        Wyślij zgłoszenie
      </button>
    </form>
  );
}
