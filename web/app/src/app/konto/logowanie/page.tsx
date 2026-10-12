import type { Metadata } from "next";
import { LoginForm } from "@/components/account/login-form";
import { accountsConfigured } from "@/lib/bff-config";

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
        <div className="notice" role="alert">
          <p>
            <strong>Konta są chwilowo niedostępne.</strong> Logowanie nie jest teraz możliwe. Przeglądanie szkół i
            wyników działa bez konta.
          </p>
        </div>
      )}
    </article>
  );
}
