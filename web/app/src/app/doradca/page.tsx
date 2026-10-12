import type { Metadata } from "next";
import Link from "next/link";
import { AdvisorChat } from "@/components/ai/advisor-chat";
import { agentBaseUrl } from "@/lib/bff-config";
import "../styles/static.css";

// Client-driven advisor. Nothing is fetched on the server: the conversation goes through the BFF /api/agent route.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Doradca wyboru szkoły",
  description:
    "Doradca AI, który sprawdza dane w portalu i opisuje, co z nich wynika. Nie ocenia szkół i nie tworzy rankingu. Obwód ustala gmina.",
  alternates: { canonical: "/doradca" },
};

export default function AdvisorPage() {
  // The advisor is off when AGENT_BASE_URL is unset (P8). The rest of the site keeps working.
  const available = agentBaseUrl() !== null;
  return (
    <article className="advisor">
      <header className="page-header ai-panel__head">
        <h1>Doradca wyboru szkoły</h1>
        <span className="badge badge--ai" aria-hidden="true">
          AI
        </span>
      </header>
      <div className="notice notice--ai" role="note">
        <p>
          <strong>Wygenerowano przez AI.</strong> Doradca korzysta z tych samych danych co reszta portalu i podaje źródła.
          Nie ocenia szkół, nie tworzy rankingu i nie zgaduje faktów, których nie ma w danych. Obwód szkolny ustala gmina.
        </p>
        <p>
          Odpowiedzi mogą zawierać błędy. Zanim podejmiesz decyzję, sprawdź profil szkoły i{" "}
          <a href="/metodologia">metodologię</a>.
        </p>
      </div>
      {available ? null : (
        <div className="notice notice--caution" role="note">
          <p>
            <strong>Doradca AI jest chwilowo niedostępny.</strong> Pozostałe funkcje strony działają bez zmian.
          </p>
          <p className="actions">
            <Link href="/">Wróć do wyszukiwarki</Link>
            <Link href="/metodologia">Przeczytaj metodologię</Link>
          </p>
        </div>
      )}
      <AdvisorChat />
    </article>
  );
}
