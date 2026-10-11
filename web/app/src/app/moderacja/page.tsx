import type { Metadata } from "next";
import { ModerationQueueView } from "@/components/moderation/moderation-queue";

// Moderator console. The page is public to render; the queue itself is only returned to moderators by the service.
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
        treści.
      </p>
      <ModerationQueueView />
    </article>
  );
}
