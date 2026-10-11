import type { Metadata } from "next";
import { CorrectionForm } from "@/components/correction-form";

export const metadata: Metadata = {
  title: "Dla szkół",
  description: "Zgłoś korektę danych szkoły. Pozycja szkoły w wynikach nie jest do kupienia.",
};

export default function ForSchoolsPage() {
  return (
    <article>
      <h1>Dla szkół</h1>
      <p>
        Jeśli prowadzisz szkołę i widzisz błędne dane, możesz zgłosić korektę. Każde zgłoszenie
        zostanie sprawdzone, a zmiana zapisana w historii wersji.
      </p>
      <div className="notice" role="note">
        <strong>Pozycja szkoły nie jest do kupienia.</strong> Płatność nie wpływa na wynik, kolejność
        ani sposób prezentacji szkoły. Płatne usługi, jeśli kiedykolwiek się pojawią, nie zmieniają
        wyników.
      </div>
      <h2>Zgłoszenie korekty</h2>
      <CorrectionForm />
    </article>
  );
}
