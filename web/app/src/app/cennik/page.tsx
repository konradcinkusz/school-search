import type { Metadata } from "next";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { INTERVAL_LABEL } from "@/components/billing/billing-messages";
import { listPlans } from "@/lib/billing";
import { formatDecimal } from "@/lib/format";
import "../styles/static.css";

// Plans come from the Billing service configuration at request time; nothing about prices is written here.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Cennik",
  description:
    "Cennik profilu rozszerzonego dla szkół. Płatność nie wpływa na wynik, ranking, kolejność ani opinie. Treść sponsorowana jest oznaczona. Nie jest oficjalnym rankingiem.",
  alternates: { canonical: "/cennik" },
};

export default async function PricingPage() {
  const result = await listPlans();

  return (
    <article className="prose">
      <header className="page-header">
        <h1>Cennik</h1>
        <p className="lead">
          Profil rozszerzony to płatna, oznaczona jako sponsorowana treść przygotowana przez przedstawiciela szkoły oraz
          formularz kontaktu dla rodziców. Wynik szkoły pozostaje taki sam, niezależnie od tego, czy szkoła ma profil.
        </p>
      </header>

      <div className="notice notice--caution" role="note">
        <p>
          <strong>Czego nie sprzedajemy:</strong>
        </p>
        <ul>
          <li>Płatny profil nie wpływa na wynik, ranking, kolejność szkoły ani na opinie użytkowników.</li>
          <li>Nie sprzedajemy wyniku, pozycji w rankingu, usuwania ani ukrywania opinii i danych.</li>
          <li>Treść sponsorowana jest zawsze oznaczona i pokazywana osobno.</li>
          <li>
            Potwierdzenie przedstawiciela przez redakcję nie jest do kupienia. Następuje po sprawdzeniu poza systemem.
          </li>
        </ul>
      </div>

      <h2>Plany</h2>
      {!result.ok && result.reason === "unavailable" ? (
        <p className="notice" role="alert">
          Płatne profile są chwilowo niedostępne.
        </p>
      ) : !result.ok ? (
        <>
          <ServiceUnavailable what="cennik" />
          <p className="actions">
            <a href="/cennik">Odśwież stronę</a>
            <a href="/dla-szkol">Dla szkół</a>
          </p>
        </>
      ) : result.data.plans.length === 0 ? (
        <p className="muted">{result.data.note ?? "Cennik w przygotowaniu."}</p>
      ) : (
        <ul className="plain-list pricing">
          {result.data.plans.map((plan) => (
            <li key={plan.code} className="card pricing__plan">
              <h3>{plan.name}</h3>
              <p>
                {plan.priceGrossPln !== null ? (
                  <>
                    <strong>{formatDecimal(plan.priceGrossPln)} zł</strong> brutto za {INTERVAL_LABEL[plan.interval] ?? plan.interval}
                  </>
                ) : (
                  <span className="muted">Cena w przygotowaniu.</span>
                )}
              </p>
            </li>
          ))}
        </ul>
      )}

      {/* Shown only when plans exist: with no plans the page already says that sales are not open yet. */}
      {result.ok && result.data.mode === "mock" && result.data.plans.length > 0 ? (
        <p className="notice notice--caution" role="note">
          <strong>Sprzedaż działa w trybie testowym.</strong> Żadna płatność nie zostanie pobrana.
        </p>
      ) : null}

      <h2>Warunki sprzedaży</h2>
      <p className="muted">
        Warunki sprzedaży są w przygotowaniu i wymagają weryfikacji prawnej. Do czasu ich zatwierdzenia nie podajemy ich
        treści.
      </p>

      <p>
        Zanim zamówisz profil, <a href="/dla-szkol">przeczytaj, jak działa zgłoszenie przedstawiciela</a>.
      </p>
    </article>
  );
}
