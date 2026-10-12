"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useEffect, useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, isUnavailable } from "@/lib/bff-client";
import { schoolYearLabel } from "@/lib/format";
import type { LegalTerms } from "@/lib/reviews-types";

/**
 * "Napisz opinię". Gated by login and a verified e-mail address. The server re-checks both on every write.
 * The statement shown here is the truth about what a review is: an opinion of a logged-in user, not a verified parent.
 */

const TITLE_MAX = 120;
const BODY_MIN = 40;
const BODY_MAX = 4000;

type Gate = "loading" | "unavailable" | "anonymous" | "unverified" | "ready";
type FieldKey = "rating" | "title" | "body" | "terms";
type FieldErrors = Partial<Record<FieldKey, string>>;
const FIELD_ORDER: readonly FieldKey[] = ["rating", "title", "body", "terms"];

const ERROR_TEXT: Record<string, string> = {
  unauthenticated: "Sesja wygasła. Zaloguj się ponownie, aby dodać opinię.",
  unauthorized: "Sesja wygasła. Zaloguj się ponownie, aby dodać opinię.",
  email_not_verified: "Potwierdź adres e-mail, aby dodać opinię.",
  duplicate_review: "Opinię dla tej szkoły w tym roku szkolnym już dodałeś.",
  terms_not_accepted: "Zaakceptuj aktualną wersję regulaminu, aby dodać opinię.",
  validation_failed: "Sprawdź dane formularza: treść, tytuł i ocena muszą spełniać podane wymagania.",
  rate_limited: "Zbyt wiele opinii w krótkim czasie. Spróbuj ponownie później.",
  reviews_unavailable: "Opinie są chwilowo niedostępne. Spróbuj ponownie za chwilę.",
  upstream_timeout: "Usługa opinii nie odpowiedziała na czas. Spróbuj ponownie.",
};

const FIELD_COPY = {
  rating: "Wybierz ocenę od 1 do 5.",
  title: `Podaj tytuł (do ${TITLE_MAX} znaków).`,
  body: `Opinia musi mieć od ${BODY_MIN} do ${BODY_MAX} znaków.`,
  terms: "Potwierdź akceptację regulaminu opinii.",
  summary: "Popraw pola oznaczone błędem i wyślij opinię ponownie.",
};

/** The last school years a review can refer to (school year 2025 means 2025/2026). */
function schoolYearOptions(now: Date): number[] {
  const startYear = now.getMonth() >= 7 ? now.getFullYear() : now.getFullYear() - 1;
  return [startYear, startYear - 1, startYear - 2];
}

export function ReviewWrite({ slug }: { readonly slug: string }) {
  const [gate, setGate] = useState<Gate>("loading");
  const [open, setOpen] = useState(false);

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest("/api/auth/session");
      if (!active) return;
      if (response === null || isUnavailable(response) || response.status !== 200) {
        setGate("unavailable");
        return;
      }
      const data = response.body as { authenticated?: unknown; emailVerified?: unknown };
      if (data.authenticated !== true) setGate("anonymous");
      else setGate(data.emailVerified === true ? "ready" : "unverified");
    })();
    return () => {
      active = false;
    };
  }, []);

  if (gate === "loading") return <p className="muted">Sprawdzanie logowania…</p>;
  if (gate === "unavailable") {
    return <p className="muted">Konta są chwilowo niedostępne, więc nie można teraz dodać opinii.</p>;
  }
  if (gate === "anonymous") {
    return (
      <p className="notice">
        Aby napisać opinię, <Link href="/konto/logowanie">zaloguj się</Link> lub{" "}
        <Link href="/konto/rejestracja">załóż konto</Link>. Opinie piszą zalogowani użytkownicy z potwierdzonym adresem
        e-mail.
      </p>
    );
  }
  if (gate === "unverified") {
    return (
      <p className="notice">
        Potwierdź adres e-mail (link w wiadomości od nas), a potem napiszesz opinię. Stan konta sprawdzisz na{" "}
        <Link href="/konto">stronie konta</Link>.
      </p>
    );
  }

  return (
    <div>
      {open ? null : (
        <button type="button" className="button" onClick={() => setOpen(true)}>
          Napisz opinię
        </button>
      )}
      {open ? <ReviewForm slug={slug} onClose={() => setOpen(false)} /> : null}
    </div>
  );
}

function ReviewForm({ slug, onClose }: { readonly slug: string; readonly onClose: () => void }) {
  const ids = {
    rating: useId(),
    ratingError: useId(),
    title: useId(),
    titleError: useId(),
    body: useId(),
    bodyError: useId(),
    year: useId(),
    terms: useId(),
    termsError: useId(),
  };
  const [years] = useState(() => schoolYearOptions(new Date()));
  const [termsVersion, setTermsVersion] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [fields, setFields] = useState<FieldErrors>({});
  const [feedback, setFeedback] = useState<{ kind: "error" | "done"; text: string } | null>(null);

  const idOf: Record<FieldKey, string> = { rating: ids.rating, title: ids.title, body: ids.body, terms: ids.terms };

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest("/api/proxy/api/v1/legal/terms");
      if (!active) return;
      const data = response?.status === 200 ? (response.body as Partial<LegalTerms>) : null;
      setTermsVersion(data && typeof data.version === "string" ? data.version : null);
    })();
    return () => {
      active = false;
    };
  }, []);

  /** Shows the errors and moves focus to the first invalid field, in form order. */
  function reject(next: FieldErrors, text: string | null) {
    setFields(next);
    setFeedback(text === null ? null : { kind: "error", text });
    const first = FIELD_ORDER.find((key) => next[key] !== undefined);
    if (first !== undefined) document.getElementById(idOf[first])?.focus();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const rating = Number(data.get("rating"));
    const title = String(data.get("title") ?? "").trim();
    const body = String(data.get("body") ?? "").trim();
    const schoolYearFrom = Number(data.get("schoolYearFrom"));
    const accepted = data.get("acceptTerms") === "on";

    const invalid: FieldErrors = {};
    if (!Number.isInteger(rating) || rating < 1 || rating > 5) invalid.rating = FIELD_COPY.rating;
    if (title === "" || title.length > TITLE_MAX) invalid.title = FIELD_COPY.title;
    if (body.length < BODY_MIN || body.length > BODY_MAX) invalid.body = FIELD_COPY.body;
    if (!accepted || termsVersion === null) invalid.terms = FIELD_COPY.terms;
    if (Object.keys(invalid).length > 0) {
      reject(invalid, FIELD_COPY.summary);
      return;
    }

    setSending(true);
    setFields({});
    setFeedback(null);
    const response = await bffRequest(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/reviews`, {
      method: "POST",
      body: { rating, title, body, schoolYearFrom, acceptedTermsVersion: termsVersion },
    });
    setSending(false);

    if (response !== null && response.status === 201) {
      setFeedback({
        kind: "done",
        text: "Dziękujemy. Opinia została przyjęta i zostanie opublikowana po sprawdzeniu przez moderatora.",
      });
      return;
    }
    const code = errorCodeOf(response?.body);
    // The status and code decide the copy. The server message is never shown; it may be English.
    const text =
      (response?.status === 401 ? ERROR_TEXT.unauthenticated : null) ||
      (response?.status === 503 ? ERROR_TEXT.reviews_unavailable : null) ||
      (code !== null && ERROR_TEXT[code]) ||
      (response === null ? "Nie udało się połączyć z usługą. Opinia nie została wysłana." : null) ||
      "Nie udało się wysłać opinii. Spróbuj ponownie.";
    setFeedback({ kind: "error", text });
  }

  return (
    <form onSubmit={onSubmit} noValidate className="card review-form">
      <h3>Nowa opinia</h3>
      <p className="muted">
        Opinia pochodzi od zalogowanego użytkownika z potwierdzonym adresem e-mail. Nie sprawdzamy, czy jesteś rodzicem
        ucznia tej szkoły. Nie podawaj imion dzieci ani nauczycieli. Opinie nie wpływają na wynik szkoły.
      </p>
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.rating}>
          Twoja ocena (od 1 do 5)<span aria-hidden="true"> *</span>
        </label>
        <select
          id={ids.rating}
          name="rating"
          defaultValue="5"
          aria-invalid={fields.rating ? true : undefined}
          aria-describedby={fields.rating ? ids.ratingError : undefined}
        >
          {[5, 4, 3, 2, 1].map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
        {fields.rating ? (
          <p id={ids.ratingError} className="field-error">
            {fields.rating}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.year}>Rok szkolny</label>
        <select id={ids.year} name="schoolYearFrom" defaultValue={years[0]}>
          {years.map((year) => (
            <option key={year} value={year}>
              {schoolYearLabel(year)}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.title}>
          Tytuł<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.title}
          name="title"
          type="text"
          maxLength={TITLE_MAX}
          required
          aria-invalid={fields.title ? true : undefined}
          aria-describedby={fields.title ? ids.titleError : undefined}
        />
        {fields.title ? (
          <p id={ids.titleError} className="field-error">
            {fields.title}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.body}>
          Treść opinii (od {BODY_MIN} do {BODY_MAX} znaków)<span aria-hidden="true"> *</span>
        </label>
        <textarea
          id={ids.body}
          name="body"
          rows={6}
          minLength={BODY_MIN}
          maxLength={BODY_MAX}
          required
          aria-invalid={fields.body ? true : undefined}
          aria-describedby={fields.body ? ids.bodyError : undefined}
        />
        {fields.body ? (
          <p id={ids.bodyError} className="field-error">
            {fields.body}
          </p>
        ) : null}
      </div>
      <div className="field field--check">
        <input
          id={ids.terms}
          name="acceptTerms"
          type="checkbox"
          required
          aria-invalid={fields.terms ? true : undefined}
          aria-describedby={fields.terms ? ids.termsError : undefined}
        />
        <label htmlFor={ids.terms}>
          Akceptuję zasady pisania opinii (<Link href="/regulamin">regulamin</Link>).
          <span aria-hidden="true"> *</span>
          {termsVersion === null ? " Wersja regulaminu jest chwilowo niedostępna." : ""}
        </label>
        {fields.terms ? (
          <p id={ids.termsError} className="field-error">
            {fields.terms}
          </p>
        ) : null}
      </div>
      <div className="school-actions">
        <button type="submit" className="button" aria-disabled={sending || termsVersion === null ? true : undefined}>
          {sending ? "Wysyłanie…" : "Wyślij opinię"}
        </button>{" "}
        <button type="button" className="link-button" onClick={onClose}>
          Anuluj
        </button>
      </div>
      {feedback !== null ? (
        <div role={feedback.kind === "error" ? "alert" : "status"} className="notice">
          <p>{feedback.text}</p>
        </div>
      ) : null}
    </form>
  );
}
