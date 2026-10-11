import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { SchoolJsonLd } from "@/components/school-jsonld";

type Props = { params: Promise<{ slug: string }> };

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  return { title: "Szkoła", description: `Strona szkoły (${slug}) w przygotowaniu.` };
}

export default async function SchoolPage({ params }: Props) {
  const { slug } = await params;
  if (!SLUG_PATTERN.test(slug) || slug.length > 120) notFound();

  // Real school data arrives with Phase C; until then nothing is rendered as if it were known.
  return (
    <article>
      <h1>Szkoła</h1>
      <div className="notice" role="note">
        Strona szkoły jest w przygotowaniu. Identyfikator: <code>{slug}</code>.
      </div>
      <p>
        Tutaj pojawią się wyniki, poziom pewności, liczebność kohorty i metodologia wersji, której
        dotyczą. Każdą wartość opatrzymy źródłem i datą.
      </p>
      <p className="muted">
        Widzisz błąd w danych tej szkoły? <a href="/dla-szkol">Zgłoś korektę</a>.
      </p>
      <SchoolJsonLd school={null} />
    </article>
  );
}
