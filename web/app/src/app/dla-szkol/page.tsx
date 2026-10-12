import type { Metadata } from "next";
import Link from "next/link";
import { CorrectionForm } from "@/components/correction-form";
import { NotSoldList } from "@/components/billing/not-sold-list";
import "../styles/static.css";

// Reads ?slug= to prefill the form; no data is fetched here.
export const dynamic = "force-dynamic";

const description =
  "Profil rozszerzony dla szkół: co można kupić, a czego nie. Płatność nie wpływa na wynik, ranking ani opinie. Zgłoś korektę danych szkoły.";

export const metadata: Metadata = {
  title: "Dla szkół",
  description,
  alternates: { canonical: "/dla-szkol" },
  openGraph: {
    type: "website",
    locale: "pl_PL",
    siteName: "Wyszukiwarka szkół",
    title: "Dla szkół | Wyszukiwarka szkół",
    description,
    url: "/dla-szkol",
  },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ForSchoolsPage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const raw = Array.isArray(sp.slug) ? sp.slug[0] : sp.slug;
  const slug = raw !== undefined && /^[a-z0-9-]{1,120}$/.test(raw) ? raw : undefined;

  return (
    <article className="prose">
      <header className="page-header">
        <p className="eyebrow">Korekty danych</p>
        <h1>Dla szkół</h1>
        <p className="lead">
          Jeśli prowadzisz szkołę albo widzisz błędne dane o szkole, możesz zgłosić korektę. Każde zgłoszenie zostanie
          sprawdzone, a zmiana zapisana w historii wersji.
        </p>
      </header>
      <div className="notice notice--caution" role="note">
        <strong>Pozycja szkoły nie jest do kupienia.</strong> Płatność nie wpływa na wynik, kolejność ani sposób
        prezentacji szkoły.
      </div>

      <h2>Profil rozszerzony</h2>
      <p>
        Dla szkoły możemy udostępnić płatny profil rozszerzony: krótki opis przygotowany przez przedstawiciela szkoły
        i formularz kontaktu dla rodziców. Profil jest zawsze oznaczony jako <strong>treść sponsorowana</strong> i pokazany
        osobno, pod wynikiem i opiniami.
      </p>

      <h3>Jak to działa</h3>
      <ol>
        <li>
          Zaloguj się, potwierdź adres e-mail i zgłoś swoją rolę w szkole na <Link href="/konto/szkola">stronie konta szkoły</Link>.
        </li>
        <li>Redakcja sprawdza zgłoszenie poza systemem. Dopiero po zatwierdzeniu możesz wykupić plan i edytować profil.</li>
        <li>Zapisany profil pojawia się na stronie szkoły z oznaczeniem treści sponsorowanej.</li>
        <li>Rodzice mogą wysłać wiadomość do szkoły. Wiadomość widzisz tylko Ty, w swoim koncie.</li>
      </ol>
      <p>
        Ceny i plany: <Link href="/cennik">cennik</Link>.
      </p>

      <h3>Czego nie sprzedajemy</h3>
      <NotSoldList
        items={[
          "Wyniku szkoły, pozycji w rankingu ani kolejności wyników.",
          "Usuwania, ograniczania ani ukrywania opinii i danych o szkole.",
          "Odznak „najlepsza”, „zweryfikowana” czy podobnych. Potwierdzenie przedstawiciela przez redakcję nie jest do kupienia.",
          "Wpływu płatności na listy, porównania, sortowanie wyników i opinie użytkowników.",
        ]}
      />

      <h2 id="korekta">Zgłoszenie korekty</h2>
      <div className="card form-card">
        <CorrectionForm initialSlug={slug} />
      </div>
    </article>
  );
}
