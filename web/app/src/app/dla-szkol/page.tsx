import type { Metadata } from "next";
import { CorrectionForm } from "@/components/correction-form";

// Reads ?slug= to prefill the form; no data is fetched here.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Dla szkół",
  description:
    "Zgłoś korektę danych szkoły. Pozycja szkoły w wynikach nie jest do kupienia. Nie jest oficjalnym rankingiem.",
  alternates: { canonical: "/dla-szkol" },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ForSchoolsPage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const raw = Array.isArray(sp.slug) ? sp.slug[0] : sp.slug;
  const slug = raw !== undefined && /^[a-z0-9-]{1,120}$/.test(raw) ? raw : undefined;

  return (
    <article>
      <h1>Dla szkół</h1>
      <p>
        Jeśli prowadzisz szkołę albo widzisz błędne dane o szkole, możesz zgłosić korektę. Każde zgłoszenie zostanie
        sprawdzone, a zmiana zapisana w historii wersji.
      </p>
      <div className="notice" role="note">
        <strong>Pozycja szkoły nie jest do kupienia.</strong> Płatność nie wpływa na wynik, kolejność ani sposób
        prezentacji szkoły.
      </div>
      <h2>Zgłoszenie korekty</h2>
      <CorrectionForm initialSlug={slug} />
    </article>
  );
}
