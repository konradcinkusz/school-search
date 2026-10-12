import type { Metadata } from "next";
import { LoginForm } from "@/components/account/login-form";
import Link from "next/link";
import { accountsConfigured } from "@/lib/bff-config";
import "../../styles/static.css";

// Availability is read per request from the environment (P8: the page degrades instead of failing).
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Logowanie",
  description: "Zaloguj się, aby napisać opinię o szkole.",
  robots: { index: false, follow: false },
  alternates: { canonical: "/konto/logowanie" },
};

export default function LoginPage() {
  const available = accountsConfigured();
  return (
    <article className="narrow">
      <h1>Logowanie</h1>
      {available ? (
        <div className="card form-card">
          <LoginForm />
        </div>
      ) : (
        <div className="notice notice--caution" role="status">
          <p>
            <strong>Konta są chwilowo niedostępne.</strong> Logowanie nie jest teraz możliwe. Przeglądanie szkół i wyników działa bez konta.
          </p>
          <p className="actions">
            <Link className="button button--secondary" href="/">
              Przeglądaj szkoły
            </Link>
            <Link href="/konto/logowanie">Spróbuj ponownie</Link>
          </p>
        </div>
      )}
    </article>
  );
}
