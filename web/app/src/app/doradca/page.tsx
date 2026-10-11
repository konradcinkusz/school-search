import type { Metadata } from "next";
import { AdvisorChat } from "@/components/ai/advisor-chat";

// Client-driven advisor. Nothing is fetched on the server: the conversation goes through the BFF /api/agent route.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Doradca wyboru szkoły",
  description:
    "Doradca AI, który sprawdza dane w portalu i opisuje, co z nich wynika. Nie ocenia szkół i nie tworzy rankingu. Obwód ustala gmina.",
  alternates: { canonical: "/doradca" },
};

export default function AdvisorPage() {
  return (
    <article>
      <h1>Doradca wyboru szkoły</h1>
      <div className="notice" role="note">
        <p>
          <strong>Wygenerowano przez AI.</strong> Doradca korzysta z tych samych danych co reszta portalu i podaje źródła.
          Nie ocenia szkół, nie tworzy rankingu i nie zgaduje faktów, których nie ma w danych. Obwód szkolny ustala gmina.
        </p>
        <p>
          Odpowiedzi mogą zawierać błędy. Zanim podejmiesz decyzję, sprawdź profil szkoły i{" "}
          <a href="/metodologia">metodologię</a>.
        </p>
      </div>
      <AdvisorChat />
    </article>
  );
}
