"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, errorListOf, errorMessageOf, isUnavailable } from "@/lib/bff-client";

/**
 * Registration through the BFF. Consent versions are fetched server-side from authservice; the browser sends only a
 * boolean acceptance. Passwords are never stored by this application.
 */
export function RegisterForm() {
  const ids = { email: useId(), password: useId(), accept: useId() };
  const router = useRouter();
  const [sending, setSending] = useState(false);
  const [feedback, setFeedback] = useState<{ kind: "error" | "info"; text: string; details: string[] } | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const email = String(data.get("email") ?? "").trim();
    const password = String(data.get("password") ?? "");
    const acceptTerms = data.get("acceptTerms") === "on";
    if (email === "" || password.length < 8 || password.length > 100) {
      setFeedback({ kind: "error", text: "Podaj adres e-mail i hasło o długości od 8 do 100 znaków.", details: [] });
      return;
    }
    if (!acceptTerms) {
      setFeedback({ kind: "error", text: "Potwierdź akceptację regulaminu i polityki prywatności.", details: [] });
      return;
    }

    setSending(true);
    setFeedback(null);
    const response = await bffRequest("/api/auth/register", { method: "POST", body: { email, password, acceptTerms } });
    setSending(false);
    if (response === null || isUnavailable(response)) {
      setFeedback({ kind: "error", text: "Konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.", details: [] });
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
        details: [],
      });
      return;
    }
    if (errorCodeOf(response.body) === "rate_limited") {
      setFeedback({ kind: "error", text: "Zbyt wiele prób. Spróbuj ponownie później.", details: [] });
      return;
    }
    setFeedback({
      kind: "error",
      text: errorMessageOf(response.body) ?? "Nie udało się założyć konta. Spróbuj ponownie.",
      details: errorListOf(response.body),
    });
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={ids.email}>Adres e-mail</label>
        <input id={ids.email} name="email" type="email" autoComplete="email" maxLength={200} required />
      </div>
      <div className="field">
        <label htmlFor={ids.password}>Hasło (od 8 do 100 znaków, wielka i mała litera, cyfra i znak)</label>
        <input id={ids.password} name="password" type="password" autoComplete="new-password" minLength={8} maxLength={100} required />
      </div>
      <div className="field field--check">
        <input id={ids.accept} name="acceptTerms" type="checkbox" required />
        <label htmlFor={ids.accept}>
          Akceptuję <Link href="/regulamin">regulamin</Link> i <Link href="/prywatnosc">politykę prywatności</Link>.
        </label>
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Zakładanie konta…" : "Załóż konto"}
      </button>
      {feedback !== null ? (
        <div role={feedback.kind === "error" ? "alert" : "status"} className="notice">
          <p>{feedback.text}</p>
          {feedback.details.length > 0 ? (
            <ul>
              {feedback.details.map((detail) => (
                <li key={detail}>{detail}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
      <p className="muted">
        Masz konto? <Link href="/konto/logowanie">Zaloguj się</Link>.
      </p>
    </form>
  );
}
