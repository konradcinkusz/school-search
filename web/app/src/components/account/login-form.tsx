"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, isUnavailable } from "@/lib/bff-client";

/**
 * Login through the BFF only: the password goes to /api/auth/login, which forwards it to authservice over TLS.
 * Nothing is stored in the browser. Two-factor login is not supported yet and says so.
 */
const MESSAGES: Record<string, string> = {
  invalid_credentials: "Nieprawidłowy adres e-mail lub hasło.",
  locked_out: "Konto jest chwilowo zablokowane po wielu nieudanych próbach. Spróbuj ponownie później.",
  email_verification_required: "Potwierdź adres e-mail (link w wiadomości), a potem zaloguj się.",
  rate_limited: "Zbyt wiele prób logowania. Spróbuj ponownie później.",
};

export function LoginForm() {
  const ids = { email: useId(), password: useId() };
  const router = useRouter();
  const [sending, setSending] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email") ?? "").trim();
    const password = String(data.get("password") ?? "");
    if (email === "" || password === "") {
      setMessage("Podaj adres e-mail i hasło.");
      return;
    }
    setSending(true);
    setMessage(null);
    const response = await bffRequest("/api/auth/login", { method: "POST", body: { email, password } });
    setSending(false);
    if (response === null || isUnavailable(response)) {
      setMessage("Konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.");
      return;
    }
    const status = typeof response.body === "object" && response.body !== null ? (response.body as { status?: unknown }).status : undefined;
    if (response.status === 200 && status === "authenticated") {
      router.push("/konto");
      router.refresh();
      return;
    }
    if (response.status === 200 && status === "two_factor_required") {
      setMessage("Logowanie dwuskładnikowe nie jest jeszcze obsługiwane.");
      return;
    }
    const code = errorCodeOf(response.body);
    setMessage((code && MESSAGES[code]) ?? "Nie udało się zalogować. Spróbuj ponownie.");
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <p className="field-hint">
        Pola oznaczone * są wymagane.
      </p>
      <div className="field">
        <label htmlFor={ids.email}>
          Adres e-mail<span aria-hidden="true"> *</span>
        </label>
        <input id={ids.email} name="email" type="email" autoComplete="email" maxLength={200} required />
      </div>
      <div className="field">
        <label htmlFor={ids.password}>
          Hasło<span aria-hidden="true"> *</span>
        </label>
        <input id={ids.password} name="password" type="password" autoComplete="current-password" maxLength={200} required />
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Logowanie…" : "Zaloguj się"}
      </button>
      {message !== null ? (
        <p role="alert" className="notice">
          {message}
        </p>
      ) : null}
      <p className="muted">
        Nie masz konta? <Link href="/konto/rejestracja">Załóż konto</Link>.
      </p>
    </form>
  );
}
