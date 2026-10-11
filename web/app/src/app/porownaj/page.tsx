import Link from "next/link";
import type { Metadata } from "next";
import { ScoreBadge } from "@/components/score-badge";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type SchoolProfile } from "@/lib/api";
import { formatStudents } from "@/lib/format";
import { firstParam, parseSlugList } from "@/lib/validation";

// Data-driven: rendered per request.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Porównaj szkoły",
  description:
    "Porównanie 2 do 4 szkół obok siebie: wynik z przedziałem, liczebność kohorty i wersja metodologii. Nie jest oficjalnym rankingiem.",
  alternates: { canonical: "/porownaj" },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function ComparePage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const raw = firstParam(sp.s, 400);
  const slugs = parseSlugList(raw);

  return (
    <section>
      <h1>Porównaj szkoły</h1>
      <p>
        Porównujesz od 2 do 4 szkół obok siebie. Każda pozycja ma wynik z przedziałem, liczebność kohorty i wersję
        metodologii.
      </p>

      <form method="get" action="/porownaj" className="address-form">
        <div className="field">
          <label htmlFor="s">Identyfikatory szkół (slug), oddzielone przecinkami</label>
          <input
            id="s"
            name="s"
            type="text"
            placeholder="np. szkola-podstawowa-nr-1-warszawa,szkola-podstawowa-nr-2-warszawa"
            defaultValue={raw ?? ""}
          />
        </div>
        <button type="submit" className="button">
          Porównaj
        </button>
      </form>
      <p className="muted">
        Identyfikator szkoły znajdziesz w adresie jej profilu. Na profilu jest też przycisk „Dodaj do porównania”.
      </p>

      {raw !== undefined && slugs === null ? (
        <div className="notice" role="alert">
          Podaj od 2 do 4 różnych identyfikatorów szkół (małe litery, cyfry i myślniki).
        </div>
      ) : null}

      {slugs ? <CompareResults slugs={slugs} /> : null}
    </section>
  );
}

async function CompareResults({ slugs }: { slugs: string[] }) {
  const result = await catalog.compare(slugs);
  if (!result.ok) {
    if (result.reason === "unavailable") return <ServiceUnavailable what="porównanie szkół" />;
    return (
      <div className="notice" role="alert">
        Nie udało się porównać tych szkół. {result.message ?? ""}
      </div>
    );
  }
  const items: readonly SchoolProfile[] = result.data.items;
  const versions = new Set(items.map((item) => item.methodologyVersion));
  const sameVersion = versions.size <= 1;

  return (
    <>
      {!sameVersion ? (
        <div className="notice" role="alert">
          Wybrane szkoły mają wyniki z różnych wersji metodologii. Nie porównujemy takich wyników bezpośrednio.
        </div>
      ) : null}
      <div className="compare-grid">
        {items.map((school) => (
          <article key={school.slug} className="card compare-card">
            <h2>
              <Link href={`/szkoly/${school.slug}`}>{school.name}</Link>
            </h2>
            <p className="muted">{school.gminaName ?? `gmina TERYT ${school.gminaTeryt}`}</p>
            <ScoreBadge
              status={school.scoreStatus}
              block={school.score}
              fallbackMethodologyVersion={school.methodologyVersion}
            />
            <p className="muted">
              Liczebność w ostatnim publikowanym roku:{" "}
              {school.score ? formatStudents(school.score.cohortSize) : "brak danych"}
            </p>
          </article>
        ))}
      </div>
      <p className="muted">
        Wersja metodologii: {[...versions].join(", ") || "brak"}. Wynik nie jest oficjalnym rankingiem.
      </p>
    </>
  );
}
