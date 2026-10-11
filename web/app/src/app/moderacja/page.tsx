import type { Metadata } from "next";
import { ModerationConsole } from "@/components/moderation/moderation-console";

// Moderator console. The page is public to render; the queues themselves are only returned to moderators by the services.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Moderacja",
  robots: { index: false, follow: false },
  alternates: { canonical: "/moderacja" },
};

export default function ModerationPage() {
  return (
    <article>
      <h1>Moderacja</h1>
      <p className="muted">
        Decyzje podejmuje człowiek. Podpowiedzi AI tylko porządkują kolejkę i nie publikują, nie usuwają ani nie odrzucają
        treści. Zatwierdzenie przedstawiciela szkoły jest jedyną drogą do publikacji profilu rozszerzonego.
      </p>
      <ModerationConsole />
    </article>
  );
}
