import type { Metadata } from "next";
import { RegisterForm } from "@/components/account/register-form";
import Link from "next/link";
import { accountsConfigured } from "@/lib/bff-config";
import "../../styles/static.css";

export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Rejestracja",
  description: "Załóż konto, aby pisać opinie o szkołach.",
  robots: { index: false, follow: false },
  alternates: { canonical: "/konto/rejestracja" },
};

export default function RegisterPage() {
  const available = accountsConfigured();
  return (
    <article className="narrow">
      <h1>Rejestracja</h1>
      <p>
        Konto jest potrzebne tylko do pisania opinii i sprawdzania ich statusu. Opinie piszą zalogowani użytkownicy z
        potwierdzonym adresem e-mail. Nie weryfikujemy, czy autor jest rodzicem ucznia danej szkoły.
      </p>
      {available ? (
        <div className="card form-card">
          <RegisterForm />
        </div>
      ) : (
        <div className="notice notice--caution" role="status">
          <p>
            <strong>Konta są chwilowo niedostępne.</strong> Rejestracja nie jest teraz możliwa. Przeglądanie szkół i wyników działa bez konta.
          </p>
          <p className="actions">
            <Link className="button button--secondary" href="/">
              Przeglądaj szkoły
            </Link>
            <Link href="/konto/rejestracja">Spróbuj ponownie</Link>
          </p>
        </div>
      )}
    </article>
  );
}
