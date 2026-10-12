"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, errorListOf, isUnavailable } from "@/lib/bff-client";

/**
 * Registration through the BFF. Consent versions are fetched server-side from authservice; the browser sends only a
 * boolean acceptance. Passwords are never stored by this application.
 */
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const EMAIL_MAX = 200;
const PASSWORD_MIN = 8;
const PASSWORD_MAX = 100;

type FieldKey = "email" | "password" | "accept";
type FieldErrors = Partial<Record<FieldKey, string>>;
const FIELD_ORDER: readonly FieldKey[] = ["email", "password", "accept"];

const COPY = {
  email: "Podaj poprawny adres e-mail.",
  password: `Hasło musi mieć od ${PASSWORD_MIN} do ${PASSWORD_MAX} znaków.`,
  accept: "Potwierdź akceptację regulaminu i polityki prywatności.",
  unavailable: "Konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.",
  rateLimited: "Zbyt wiele prób. Spróbuj ponownie później.",
  generic: "Nie udało się założyć konta. Spróbuj ponownie.",
  checkFields: "Popraw pola oznaczone błędem i spróbuj ponownie.",
};

export function RegisterForm() {
  const ids = {
    email: useId(),
    emailError: useId(),
    password: useId(),
    passwordHint: useId(),
    passwordError: useId(),
    accept: useId(),
    acceptError: useId(),
  };
  const router = useRouter();
  const [sending, setSending] = useState(false);
  const [fields, setFields] = useState<FieldErrors>({});
  const [feedback, setFeedback] = useState<{ kind: "error" | "info"; text: string } | null>(null);

  const idOf: Record<FieldKey, string> = { email: ids.email, password: ids.password, accept: ids.accept };

  /** Shows the errors and moves focus to the first invalid field, in form order. */
  function reject(nextFields: FieldErrors, text: string) {
    setFields(nextFields);
    setFeedback({ kind: "error", text });
    const first = FIELD_ORDER.find((key) => nextFields[key] !== undefined);
    if (first !== undefined) document.getElementById(idOf[first])?.focus();
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email") ?? "").trim();
    const password = String(data.get("password") ?? "");
    const acceptTerms = data.get("acceptTerms") === "on";

    const clientFields: FieldErrors = {};
    if (email === "" || email.length > EMAIL_MAX || !EMAIL_SHAPE.test(email)) clientFields.email = COPY.email;
    if (password.length < PASSWORD_MIN || password.length > PASSWORD_MAX) clientFields.password = COPY.password;
    if (!acceptTerms) clientFields.accept = COPY.accept;
    if (Object.keys(clientFields).length > 0) {
      reject(clientFields, COPY.checkFields);
      return;
    }

    setSending(true);
    setFields({});
    setFeedback(null);
    const response = await bffRequest("/api/auth/register", { method: "POST", body: { email, password, acceptTerms } });
    setSending(false);
    if (response === null || isUnavailable(response)) {
      setFeedback({ kind: "error", text: COPY.unavailable });
      return;
    }
    const status = typeof response.body === "object" && response.body !== null ? (response.body as { status?: unknown }).status : undefined;
    if (response.status === 200 && status === "authenticated") {
      router.push("/konto");
      router.refresh();
      return;
    }
    if (response.status === 202 && status === "verification_required") {
      setFeedback({
        kind: "info",
        text: "Konto zostało założone. Wysłaliśmy wiadomość z linkiem potwierdzającym adres e-mail. Potwierdź adres, a potem zaloguj się.",
      });
      return;
    }
    if (errorCodeOf(response.body) === "rate_limited") {
      setFeedback({ kind: "error", text: COPY.rateLimited });
      return;
    }
    // The service's own messages are English and are read only to pick a field; the copy shown is fixed Polish.
    const serverMessages = errorListOf(response.body);
    const serverFields: FieldErrors = {};
    if (serverMessages.some((text) => /e-?mail/i.test(text))) serverFields.email = COPY.email;
    if (serverMessages.some((text) => /password/i.test(text))) serverFields.password = COPY.password;
    if (Object.keys(serverFields).length > 0) {
      reject(serverFields, COPY.checkFields);
      return;
    }
    setFeedback({ kind: "error", text: COPY.generic });
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.email}>
          Adres e-mail<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.email}
          name="email"
          type="email"
          autoComplete="email"
          maxLength={EMAIL_MAX}
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
        <label htmlFor={ids.password}>
          Hasło<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.password}
          name="password"
          type="password"
          autoComplete="new-password"
          minLength={PASSWORD_MIN}
          maxLength={PASSWORD_MAX}
          required
          aria-invalid={fields.password ? true : undefined}
          aria-describedby={fields.password ? `${ids.passwordHint} ${ids.passwordError}` : ids.passwordHint}
        />
        <p id={ids.passwordHint} className="field-hint">
          Od {PASSWORD_MIN} do {PASSWORD_MAX} znaków.
        </p>
        {fields.password ? (
          <p id={ids.passwordError} className="field-error">
            {fields.password}
          </p>
        ) : null}
      </div>
      <div className="field field--check">
        <input
          id={ids.accept}
          name="acceptTerms"
          type="checkbox"
          required
          aria-invalid={fields.accept ? true : undefined}
          aria-describedby={fields.accept ? ids.acceptError : undefined}
        />
        <label htmlFor={ids.accept}>
          Akceptuję <Link href="/regulamin">regulamin</Link> i <Link href="/prywatnosc">politykę prywatności</Link>.
          <span aria-hidden="true"> *</span>
        </label>
        {fields.accept ? (
          <p id={ids.acceptError} className="field-error">
            {fields.accept}
          </p>
        ) : null}
      </div>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Zakładanie konta…" : "Załóż konto"}
      </button>
      {feedback !== null ? (
        <div role={feedback.kind === "error" ? "alert" : "status"} className="notice">
          <p>{feedback.text}</p>
        </div>
      ) : null}
      <p className="muted">
        Masz konto? <Link href="/konto/logowanie">Zaloguj się</Link>.
      </p>
    </form>
  );
}
