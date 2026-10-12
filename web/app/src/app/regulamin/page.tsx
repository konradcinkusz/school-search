import type { Metadata } from "next";
import { legalTerms } from "@/lib/reviews";
import "../styles/static.css";

// DRAFT terms. The text is a working draft for legal review and is not legal advice (SPEC-E).
export const dynamic = "force-dynamic";

const description =
  "Regulamin kont i opinii w wyszukiwarce szkół. Projekt do weryfikacji przez prawnika.";

export const metadata: Metadata = {
  title: "Regulamin",
  description,
  alternates: { canonical: "/regulamin" },
  // Draft text: kept out of search results until the owner and a lawyer approve it.
  robots: { index: false, follow: true },
  openGraph: {
    type: "website",
    locale: "pl_PL",
    siteName: "Wyszukiwarka szkół",
    title: "Regulamin | Wyszukiwarka szkół",
    description,
    url: "/regulamin",
  },
};

export default async function TermsPage() {
  const terms = await legalTerms();
  const version = terms.ok ? terms.data.version : null;
  return (
    <article className="prose">
      <header className="page-header">
        <p className="eyebrow">Informacje prawne</p>
        <h1>Regulamin</h1>
        <p className="lead">Zasady kont i opinii w wyszukiwarce szkół.</p>
      </header>
      <div className="notice" role="note">
        <strong>Projekt do weryfikacji przez prawnika.</strong> Ten tekst jest roboczą wersją i nie jest poradą prawną.
      </div>
      <p className="muted">
        Wersja regulaminu: {version ?? "chwilowo niedostępna. Spróbuj ponownie później."}
      </p>

      <h2>1. Zakres</h2>
      <p>
        Serwis publikuje informacje o szkołach z publicznych zbiorów danych oraz opinie użytkowników. Wyniki szkół nie są
        oficjalnym rankingiem i nie są oceną nauczycieli ani uczniów.
      </p>

      <h2>2. Konto</h2>
      <p>
        Konto służy do pisania opinii. Logowanie i konto prowadzi zewnętrzny serwis uwierzytelniania. Opinie mogą pisać
        wyłącznie zalogowani użytkownicy z potwierdzonym adresem e-mail.
      </p>

      <h2>3. Opinie użytkowników</h2>
      <p>
        Opinia jest wypowiedzią użytkownika, a nie wynikiem weryfikacji. Nie sprawdzamy, czy autor jest rodzicem ucznia
        danej szkoły. Opinie nie wpływają na wynik, ranking ani wyjaśnienie AI. Zakazane są treści niezgodne z prawem,
        dane osobowe dzieci i pracowników, obraźliwe treści oraz treści reklamowe.
      </p>
      <p>Opinie są wstępnie sprawdzane przed publikacją. Każda opinia może zostać odrzucona albo usunięta.</p>

      <h2>4. Zgłaszanie treści (art. 16 DSA)</h2>
      <p>
        Każdy może zgłosić treść niezgodną z prawem lub regulaminem przez formularz <a href="/zglos-tresc">Zgłoś treść</a>.
        Zgłoszenie wymaga uzasadnienia i potwierdzenia dobrej wiary.
      </p>

      <h2>5. Decyzje i odwołania (art. 17 i 20 DSA)</h2>
      <p>
        Decyzja o treści jest zawsze podejmowana przez człowieka i zawiera uzasadnienie: podstawę, okoliczności i informację
        o odwołaniu. Od decyzji można złożyć odwołanie w serwisie.
      </p>

      <h2>6. Kontakt</h2>
      <p>
        Punkt kontaktowy jest opisany na stronie <a href="/kontakt">Kontakt</a>.
      </p>
      <p className="notice notice--caution" role="note">
        Ostatnia aktualizacja: [do uzupełnienia przez właściciela serwisu].
      </p>
    </article>
  );
}
