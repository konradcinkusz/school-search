"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorMessageOf, isUnavailable, type BffResponse } from "@/lib/bff-client";
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
const REFERENCE_MAX = 500;
const NAME_MAX = 120;
const EMAIL_MAX = 200;
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type FieldKey = "reference" | "category" | "explanation" | "name" | "email" | "goodFaith";
type FieldErrors = Partial<Record<FieldKey, string>>;
const FIELD_ORDER: readonly FieldKey[] = ["reference", "category", "explanation", "name", "email", "goodFaith"];

/** Every message the user can see. Server text is never shown; it is only read to choose one of these. */
const COPY = {
  reference: `Podaj adres treści albo jej identyfikator (do ${REFERENCE_MAX} znaków).`,
  category: "Wybierz, czego dotyczy zgłoszenie, z listy.",
  explanation: `Uzasadnij zgłoszenie (od ${EXPLANATION_MIN} do ${EXPLANATION_MAX} znaków).`,
  name: `Imię lub pseudonim może mieć najwyżej ${NAME_MAX} znaków.`,
  email: "Podaj poprawny adres e-mail albo zostaw pole puste.",
  goodFaith: "Potwierdź, że zgłoszenie składasz w dobrej wierze.",
  summary: "Popraw pola oznaczone błędem i wyślij zgłoszenie ponownie.",
  unavailable: "Usługa zgłoszeń jest chwilowo niedostępna. Zgłoszenie nie zostało wysłane.",
  rateLimited: "Wysłano zbyt wiele zgłoszeń. Spróbuj ponownie później.",
  notFound: "Nie znaleziono opinii, której dotyczy zgłoszenie. Wróć na stronę szkoły i spróbuj ponownie.",
  generic: "Zgłoszenie nie zostało przyjęte. Sprawdź dane i spróbuj ponownie.",
};

/** The server's first word names the rejected field (for example "submitterEmail is not a valid address."). */
const SERVER_FIELD: Partial<Record<string, FieldKey>> = {
  contentReference: "reference",
  category: "category",
  explanation: "explanation",
  goodFaithConfirmed: "goodFaith",
  submitterName: "name",
  submitterEmail: "email",
};

type Errors = { fields: FieldErrors; text: string | null };

function failureFor(response: BffResponse | null): Errors {
  if (response === null || isUnavailable(response)) return { fields: {}, text: COPY.unavailable };
  if (response.status === 429) return { fields: {}, text: COPY.rateLimited };
  if (response.status === 404) return { fields: {}, text: COPY.notFound };
  if (response.status === 400) {
    const key = (errorMessageOf(response.body) ?? "").split(" ")[0] ?? "";
    const field = SERVER_FIELD[key];
    if (field !== undefined) {
      const fields: FieldErrors = {};
      fields[field] = COPY[field];
      return { fields, text: COPY.summary };
    }
  }
  return { fields: {}, text: COPY.generic };
}

export function NoticeForm({
  initialReference,
  reviewId,
}: {
  readonly initialReference: string;
  readonly reviewId: string | undefined;
}) {
  const ids = {
    reference: useId(),
    referenceError: useId(),
    category: useId(),
    categoryError: useId(),
    explanation: useId(),
    explanationError: useId(),
    name: useId(),
    nameError: useId(),
    email: useId(),
    emailError: useId(),
    good: useId(),
    goodError: useId(),
  };
  const [sending, setSending] = useState(false);
  const [created, setCreated] = useState<NoticeCreated | null>(null);
  const [errors, setErrors] = useState<Errors>({ fields: {}, text: null });

  const idOf: Record<FieldKey, string> = {
    reference: ids.reference,
    category: ids.category,
    explanation: ids.explanation,
    name: ids.name,
    email: ids.email,
    goodFaith: ids.good,
  };

  /** Shows the errors and moves focus to the first invalid field, in form order. */
  function reject(next: Errors) {
    setErrors(next);
    const first = FIELD_ORDER.find((key) => next.fields[key] !== undefined);
    if (first !== undefined) document.getElementById(idOf[first])?.focus();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const contentReference = String(data.get("contentReference") ?? "").trim();
    const category = String(data.get("category") ?? "");
    const explanation = String(data.get("explanation") ?? "").trim();
    const submitterName = String(data.get("submitterName") ?? "").trim();
    const submitterEmail = String(data.get("submitterEmail") ?? "").trim();
    const goodFaith = data.get("goodFaith") === "on";

    const clientFields: FieldErrors = {};
    if (contentReference === "" || contentReference.length > REFERENCE_MAX) clientFields.reference = COPY.reference;
    if (explanation.length < EXPLANATION_MIN || explanation.length > EXPLANATION_MAX) clientFields.explanation = COPY.explanation;
    if (submitterName.length > NAME_MAX) clientFields.name = COPY.name;
    if (submitterEmail !== "" && (submitterEmail.length > EMAIL_MAX || !EMAIL_SHAPE.test(submitterEmail))) {
      clientFields.email = COPY.email;
    }
    if (!goodFaith) clientFields.goodFaith = COPY.goodFaith;
    if (Object.keys(clientFields).length > 0) {
      reject({ fields: clientFields, text: COPY.summary });
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
    setErrors({ fields: {}, text: null });
    const response = await bffRequest("/api/proxy/api/v1/notices", { method: "POST", body });
    setSending(false);

    if (response !== null && response.status === 201 && isNoticeCreated(response.body)) {
      setCreated(response.body);
      return;
    }
    reject(failureFor(response));
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
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.reference}>
          Adres treści lub jej identyfikator<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.reference}
          name="contentReference"
          type="text"
          maxLength={REFERENCE_MAX}
          defaultValue={initialReference}
          required
          aria-invalid={errors.fields.reference ? true : undefined}
          aria-describedby={errors.fields.reference ? ids.referenceError : undefined}
        />
        {errors.fields.reference ? (
          <p id={ids.referenceError} className="field-error">
            {errors.fields.reference}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.category}>Czego dotyczy zgłoszenie?</label>
        <select
          id={ids.category}
          name="category"
          defaultValue="illegal_content"
          aria-invalid={errors.fields.category ? true : undefined}
          aria-describedby={errors.fields.category ? ids.categoryError : undefined}
        >
          {NOTICE_CATEGORIES.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        {errors.fields.category ? (
          <p id={ids.categoryError} className="field-error">
            {errors.fields.category}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.explanation}>
          Dlaczego treść jest niezgodna z prawem lub regulaminem?<span aria-hidden="true"> *</span>
        </label>
        <textarea
          id={ids.explanation}
          name="explanation"
          rows={6}
          maxLength={EXPLANATION_MAX}
          required
          aria-invalid={errors.fields.explanation ? true : undefined}
          aria-describedby={errors.fields.explanation ? ids.explanationError : undefined}
        />
        {errors.fields.explanation ? (
          <p id={ids.explanationError} className="field-error">
            {errors.fields.explanation}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.name}>Imię lub pseudonim (opcjonalnie)</label>
        <input
          id={ids.name}
          name="submitterName"
          type="text"
          maxLength={NAME_MAX}
          autoComplete="name"
          aria-invalid={errors.fields.name ? true : undefined}
          aria-describedby={errors.fields.name ? ids.nameError : undefined}
        />
        {errors.fields.name ? (
          <p id={ids.nameError} className="field-error">
            {errors.fields.name}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.email}>Adres e-mail do informacji o decyzji (opcjonalnie)</label>
        <input
          id={ids.email}
          name="submitterEmail"
          type="email"
          maxLength={EMAIL_MAX}
          autoComplete="email"
          aria-invalid={errors.fields.email ? true : undefined}
          aria-describedby={errors.fields.email ? ids.emailError : undefined}
        />
        {errors.fields.email ? (
          <p id={ids.emailError} className="field-error">
            {errors.fields.email}
          </p>
        ) : null}
      </div>
      <div className="field field--check">
        <input
          id={ids.good}
          name="goodFaith"
          type="checkbox"
          required
          aria-invalid={errors.fields.goodFaith ? true : undefined}
          aria-describedby={errors.fields.goodFaith ? ids.goodError : undefined}
        />
        <label htmlFor={ids.good}>
          Potwierdzam, że zgłoszenie składam w dobrej wierze, a podane informacje są zgodne z moją wiedzą.
          <span aria-hidden="true"> *</span>
        </label>
        {errors.fields.goodFaith ? (
          <p id={ids.goodError} className="field-error">
            {errors.fields.goodFaith}
          </p>
        ) : null}
      </div>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Wysyłanie…" : "Wyślij zgłoszenie"}
      </button>
      {errors.text !== null ? (
        <div role="alert" className="notice">
          <p>{errors.text}</p>
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
