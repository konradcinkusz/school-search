import type { Metadata } from "next";

// DRAFT privacy notice. Working text for legal review, not legal advice (SPEC-E).
export const metadata: Metadata = {
  title: "Prywatność",
  description: "Jakie dane zbiera serwis, po co i jak długo. Projekt do weryfikacji przez prawnika.",
  alternates: { canonical: "/prywatnosc" },
};

export default function PrivacyPage() {
  return (
    <article className="prose">
      <h1>Polityka prywatności</h1>
      <div className="notice" role="note">
        <strong>Projekt do weryfikacji przez prawnika.</strong> Ten tekst jest roboczą wersją i nie jest poradą prawną.
      </div>

      <h2>Konto</h2>
      <p>
        Dane konta (adres e-mail i hasło) przechowuje zewnętrzny serwis uwierzytelniania. Serwis wyszukiwarki nie
        przechowuje hasła. Adres e-mail nie jest zapisywany w bazie opinii.
      </p>

      <h2>Sesja</h2>
      <p>
        Po zalogowaniu zapisujemy zaszyfrowany plik cookie <span className="token">ss_session</span>, dostępny tylko dla
        serwera. Jest niezbędny do działania konta. Nie używamy cookies reklamowych.
      </p>

      <h2>Opinie</h2>
      <p>
        Opinia zawiera ocenę, tytuł, treść, rok szkolny i miesiąc. Identyfikator autora jest przechowywany jako skrót
        kryptograficzny, którego nie da się odwrócić bez osobnego klucza. Nazwisko i adres e-mail autora nie są publikowane.
      </p>

      <h2>Zgłoszenia</h2>
      <p>
        Zgłoszenie treści może zawierać adres e-mail zgłaszającego, jeśli go podał. Służy wyłącznie do poinformowania o
        decyzji.
      </p>

      <h2>Okres przechowywania</h2>
      <p>[do uzupełnienia przez właściciela serwisu i prawnika].</p>

      <h2>Twoje prawa</h2>
      <p>
        Masz prawo dostępu do danych, ich sprostowania, usunięcia i sprzeciwu. Skontaktuj się z nami przez{" "}
        <a href="/kontakt">punkt kontaktowy</a>.
      </p>
    </article>
  );
}
