import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { isZasieg, ZASIEG_LABEL } from "@/lib/zasieg";

type Props = { params: Promise<{ zasieg: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { zasieg } = await params;
  if (!isZasieg(zasieg)) return { title: "Ranking" };
  return {
    title: `Ranking szkół — ${ZASIEG_LABEL[zasieg]}`,
    description: `Ranking szkół podstawowych w zasięgu: ${ZASIEG_LABEL[zasieg]}. Nie jest oficjalnym rankingiem.`,
  };
}

export default async function RankingPage({ params }: Props) {
  const { zasieg } = await params;
  if (!isZasieg(zasieg)) notFound();

  // Only descending order is offered by design; there is deliberately no sort control
  // that would present the weakest schools first.
  return (
    <section>
      <h1>Ranking szkół podstawowych</h1>
      <p>
        Zasięg: <strong>{ZASIEG_LABEL[zasieg]}</strong>
      </p>
      <p className="muted">Kolejność: od najwyższego wyniku. Wyniki bez wystarczającej kohorty nie są oceniane.</p>
      <div className="notice" role="note">
        Lista jest w przygotowaniu. Ranking nie jest oficjalnym rankingiem i nie zastępuje
        decyzji rodziców. Zasady są opisane w{" "}
        <a href="/metodologia">metodologii</a>.
      </div>
    </section>
  );
}
