"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useId, useState, type FormEvent } from "react";
import { errorMessageOf } from "@/lib/bff-client";

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

/** Preschool corrections (SPEC-G, G8): the catalog accepts kind=preschool with these field codes only. */
const PRESCHOOL_FIELD_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "tuition_min", label: "Czesne: minimum (zł miesięcznie)" },
  { value: "tuition_max", label: "Czesne: maksimum (zł miesięcznie)" },
  { value: "hours", label: "Godziny otwarcia" },
  { value: "free_places", label: "Wolne miejsca" },
  { value: "address", label: "Adres" },
  { value: "other", label: "Inne" },
];

type Target = "school" | "preschool";
type FieldKey = "slug" | "field" | "message" | "email";
type FieldErrors = Partial<Record<FieldKey, string>>;

const MESSAGE_MIN = 10;
const MESSAGE_MAX = 2000;
const EMAIL_MAX = 200;
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const EMAIL_SHAPE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const FIELD_ORDER: readonly FieldKey[] = ["slug", "field", "message", "email"];

/** Every message the user can see. Server text is never shown; it is only read to choose one of these. */
const COPY = {
  slugMissing: "Podaj identyfikator z adresu profilu, na przykład szkola-podstawowa-nr-1.",
  slugFormat: "Identyfikator zapisz małymi literami, cyframi i myślnikami, na przykład szkola-podstawowa-nr-1.",
  slugSchoolNotFound: "Nie znaleziono takiej szkoły. Sprawdź identyfikator z adresu jej profilu.",
  slugPreschoolNotFound: "Nie znaleziono takiego przedszkola. Sprawdź identyfikator z adresu jego profilu.",
  field: "Wybierz, czego dotyczy korekta, z listy.",
  message: `Opis korekty musi mieć od ${MESSAGE_MIN} do ${MESSAGE_MAX} znaków.`,
  email: "Podaj poprawny adres e-mail albo zostaw pole puste.",
  summary: "Popraw pola oznaczone błędem i wyślij zgłoszenie ponownie.",
  rateLimited: "Wysłano zbyt wiele zgłoszeń. Spróbuj ponownie za chwilę.",
  unavailable: "Usługa jest chwilowo niedostępna. Zgłoszenie nie zostało wysłane. Spróbuj ponownie.",
  network: "Nie udało się połączyć z usługą. Zgłoszenie nie zostało wysłane.",
  generic: "Zgłoszenie nie zostało przyjęte. Sprawdź dane i spróbuj ponownie.",
};

type Submitted = { token: string; status: string };
type Errors = { fields: FieldErrors; text: string | null };

/** The server's first word names the field it rejected (for example "contactEmail is not a valid address."). */
function serverFieldKey(payload: unknown): string {
  return (errorMessageOf(payload) ?? "").split(" ")[0] ?? "";
}

/** Maps a failed response to fixed Polish copy, per field where the server names one. */
function failureFor(status: number, payload: unknown, target: Target): Errors {
  if (status === 429) return { fields: {}, text: COPY.rateLimited };
  if (status === 503) return { fields: {}, text: COPY.unavailable };
  if (status === 404) {
    const notFound = target === "preschool" ? COPY.slugPreschoolNotFound : COPY.slugSchoolNotFound;
    return { fields: { slug: notFound }, text: COPY.summary };
  }
  if (status === 400) {
    const key = serverFieldKey(payload);
    if (key === "slug") {
      const text = (errorMessageOf(payload) ?? "").includes("required") ? COPY.slugMissing : COPY.slugFormat;
      return { fields: { slug: text }, text: COPY.summary };
    }
    if (key === "field") return { fields: { field: COPY.field }, text: COPY.summary };
    if (key === "message") return { fields: { message: COPY.message }, text: COPY.summary };
    if (key === "contactEmail") return { fields: { email: COPY.email }, text: COPY.summary };
  }
  return { fields: {}, text: COPY.generic };
}

export function CorrectionForm({ initialSlug }: { initialSlug: string | undefined }) {
  const ids = {
    slug: useId(),
    slugError: useId(),
    target: useId(),
    field: useId(),
    fieldError: useId(),
    message: useId(),
    messageError: useId(),
    email: useId(),
    emailError: useId(),
  };
  const [sending, setSending] = useState(false);
  const [errors, setErrors] = useState<Errors>({ fields: {}, text: null });
  const [submitted, setSubmitted] = useState<Submitted | null>(null);
  const [target, setTarget] = useState<Target>("school");
  const fieldOptions = target === "preschool" ? PRESCHOOL_FIELD_OPTIONS : FIELD_OPTIONS;

  const idOf: Record<FieldKey, string> = {
    slug: ids.slug,
    field: ids.field,
    message: ids.message,
    email: ids.email,
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
    const form = event.currentTarget;
    const data = new FormData(form);
    const slug = String(data.get("slug") ?? "").trim();
    const field = String(data.get("field") ?? "").trim();
    const message = String(data.get("message") ?? "").trim();
    const email = String(data.get("contactEmail") ?? "").trim();

    const clientFields: FieldErrors = {};
    if (slug === "") clientFields.slug = COPY.slugMissing;
    else if (!SLUG_PATTERN.test(slug) || slug.length > 120) clientFields.slug = COPY.slugFormat;
    if (message.length < MESSAGE_MIN || message.length > MESSAGE_MAX) clientFields.message = COPY.message;
    if (email !== "" && (email.length > EMAIL_MAX || !EMAIL_SHAPE.test(email))) clientFields.email = COPY.email;
    if (Object.keys(clientFields).length > 0) {
      reject({ fields: clientFields, text: COPY.summary });
      return;
    }

    const body: Record<string, string> = { slug, field, message };
    if (target === "preschool") body.kind = "preschool";
    if (email !== "") body.contactEmail = email;

    setSending(true);
    setErrors({ fields: {}, text: null });
    setSubmitted(null);
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
          setSubmitted({ token, status });
          form.reset();
          setTarget("school");
          return;
        }
      }
      reject(failureFor(response.status, payload, target));
    } catch {
      setErrors({ fields: {}, text: COPY.network });
    } finally {
      setSending(false);
    }
  }

  const slugLabel =
    target === "preschool"
      ? "Identyfikator przedszkola z adresu jego profilu (np. przedszkole-demonstracyjne-nr-1)"
      : "Identyfikator szkoły z adresu jej profilu (np. szkola-podstawowa-nr-1)";

  return (
    <form onSubmit={onSubmit} noValidate>
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.target}>Czego dotyczy zgłoszenie?</label>
        <select
          id={ids.target}
          name="target"
          value={target}
          onChange={(event) => setTarget(event.target.value === "preschool" ? "preschool" : "school")}
        >
          <option value="school">Szkoły podstawowej</option>
          <option value="preschool">Przedszkola</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.slug}>
          {slugLabel}
          <span aria-hidden="true">&nbsp;*</span>
        </label>
        <input
          id={ids.slug}
          name="slug"
          type="text"
          defaultValue={initialSlug ?? ""}
          maxLength={120}
          autoComplete="off"
          required
          aria-invalid={errors.fields.slug ? true : undefined}
          aria-describedby={errors.fields.slug ? ids.slugError : undefined}
        />
        {errors.fields.slug ? (
          <p id={ids.slugError} className="field-error">
            {errors.fields.slug}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.field}>Czego dotyczy korekta?</label>
        <select
          key={target}
          id={ids.field}
          name="field"
          defaultValue="other"
          aria-invalid={errors.fields.field ? true : undefined}
          aria-describedby={errors.fields.field ? ids.fieldError : undefined}
        >
          {fieldOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        {errors.fields.field ? (
          <p id={ids.fieldError} className="field-error">
            {errors.fields.field}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.message}>
          Co jest błędne i jakie są właściwe dane?<span aria-hidden="true">&nbsp;*</span>
        </label>
        <textarea
          id={ids.message}
          name="message"
          rows={5}
          maxLength={MESSAGE_MAX}
          required
          aria-invalid={errors.fields.message ? true : undefined}
          aria-describedby={errors.fields.message ? ids.messageError : undefined}
        />
        {errors.fields.message ? (
          <p id={ids.messageError} className="field-error">
            {errors.fields.message}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.email}>Adres e-mail do kontaktu (opcjonalnie)</label>
        <input
          id={ids.email}
          name="contactEmail"
          type="email"
          autoComplete="email"
          maxLength={EMAIL_MAX}
          aria-invalid={errors.fields.email ? true : undefined}
          aria-describedby={errors.fields.email ? ids.emailError : undefined}
        />
        {errors.fields.email ? (
          <p id={ids.emailError} className="field-error">
            {errors.fields.email}
          </p>
        ) : null}
      </div>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Wysyłanie…" : "Wyślij zgłoszenie"}
      </button>

      {errors.text !== null ? (
        <p role="alert" className="notice">
          {errors.text}
        </p>
      ) : null}
      {submitted !== null ? (
        <div role="status" className="notice">
          <p>
            <strong>Zgłoszenie przyjęte.</strong> Status: nowe. Zachowaj poniższy token, aby sprawdzić status zgłoszenia.
          </p>
          <p>
            Token: <code className="token">{submitted.token}</code>
          </p>
          <p>
            <Link href={`/dla-szkol/zgloszenie/${encodeURIComponent(submitted.token)}`}>Sprawdź status zgłoszenia</Link>
          </p>
        </div>
      ) : null}
    </form>
  );
}
