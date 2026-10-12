import Link from "next/link";
import type { Metadata } from "next";
import "@/app/styles/lists.css";
import { ScoreBadge } from "@/components/score-badge";
import { FocusOnResults } from "@/components/focus-on-results";
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
  const raw = firstParam(sp.slugs, 400) ?? firstParam(sp.s, 400);
  const slugs = parseSlugList(raw);

  return (
    <section>
      <header className="page-header">
        <h1>Porównaj szkoły</h1>
        <p className="lead">
          Porównujesz od 2 do 4 szkół obok siebie. Każda pozycja ma wynik z przedziałem, liczebność kohorty i wersję
          metodologii.
        </p>
      </header>

      <div className="card">
        <form method="get" action="/porownaj" className="inline-form">
          <div className="field">
            <label htmlFor="slugs">Szkoły do porównania, oddzielone przecinkami</label>
            <input
              id="slugs"
              name="slugs"
              type="text"
              placeholder="np. szkola-podstawowa-nr-1-warszawa, szkola-podstawowa-nr-2-warszawa"
              defaultValue={raw ?? ""}
            />
          </div>
          <button type="submit" className="button">
            Porównaj
          </button>
        </form>
        <p className="muted field-hint compare-hint">
          Identyfikator szkoły znajdziesz w adresie jej profilu. Na profilu jest też przycisk „Dodaj do porównania”.
        </p>
      </div>

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
    // Preschools have their own list and are not compared with primary schools: say so instead of "not found".
    const preschoolLookups = await Promise.all(slugs.map((slug) => catalog.preschool(slug)));
    if (preschoolLookups.some((lookup) => lookup.ok)) {
      return (
        <div className="notice" role="alert">
          To jest przedszkole. Porównujemy szkoły podstawowe; przedszkola znajdziesz na{" "}
          <Link href="/przedszkola">liście przedszkoli</Link>.
        </div>
      );
    }
    return (
      <div className="notice" role="alert">
        Nie udało się porównać tych szkół. {result.message ?? ""}
      </div>
    );
  }
  const items: readonly SchoolProfile[] = result.data.items;
  const versions = new Set(items.map((item) => item.methodologyVersion));
  const sameVersion = versions.size <= 1;
  // Removing a school is a link to the same page without its ID. Two schools is the minimum, so none is removed then.
  const canRemove = items.length > 2;

  return (
    <>
      <h2 id="wynik-porownania" className="compare-result-heading" tabIndex={-1}>
        Wynik porównania
      </h2>
      <FocusOnResults targetId="wynik-porownania" />
      {!sameVersion ? (
        <div className="notice notice--caution" role="alert">
          Wybrane szkoły mają wyniki z różnych wersji metodologii. Nie porównujemy takich wyników bezpośrednio.
        </div>
      ) : null}
      <ul className="compare-chips" aria-label="Wybrane szkoły">
        {items.map((school) => {
          const others = items.filter((item) => item.slug !== school.slug).map((item) => item.slug);
          return (
            <li key={school.slug} className="compare-chip">
              <span>{school.name}</span>
              {canRemove ? (
                <Link
                  href={`/porownaj?slugs=${others.map(encodeURIComponent).join(",")}`}
                  className="compare-chip__remove"
                  aria-label={`Usuń z porównania: ${school.name}`}
                >
                  ×
                </Link>
              ) : null}
            </li>
          );
        })}
      </ul>
      <p id="compare-caption" className="muted">
        Porównanie szkół: te same cechy dla każdej szkoły.
      </p>
      <p className="compare-scroll-hint muted">Przesuń tabelę w bok, aby zobaczyć wszystkie szkoły.</p>
      <div className="compare-scroll">
        <div
          className="table-wrap table-wrap--compare"
          role="region"
          aria-label="Tabela porównania (przewiń w bok na małym ekranie)"
          tabIndex={0}
        >
          <table className="table--sticky table--compare" aria-describedby="compare-caption">
            <thead>
              <tr>
                <th scope="col">Cecha</th>
                {items.map((school) => (
                  <th scope="col" key={school.slug}>
                    <Link href={`/szkoly/${school.slug}`}>{school.name}</Link>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr>
                <th scope="row">Wynik z przedziałem</th>
                {items.map((school) => (
                  <td key={school.slug}>
                    <ScoreBadge
                      status={school.scoreStatus}
                      block={school.score}
                      fallbackMethodologyVersion={school.methodologyVersion}
                      variant="compact"
                    />
                  </td>
                ))}
              </tr>
              <tr>
                <th scope="row">Liczebność kohorty</th>
                {items.map((school) => (
                  <td key={school.slug}>
                    {school.score ? formatStudents(school.score.cohortSize) : "brak danych"}
                  </td>
                ))}
              </tr>
              <tr>
                <th scope="row">Rok egzaminu</th>
                {items.map((school) => (
                  <td key={school.slug}>{school.score ? school.score.examYear : "brak danych"}</td>
                ))}
              </tr>
              <tr>
                <th scope="row">Gmina</th>
                {items.map((school) => (
                  <td key={school.slug}>{school.gminaName ?? `TERYT ${school.gminaTeryt}`}</td>
                ))}
              </tr>
              <tr>
                <th scope="row">Rodzaj szkoły</th>
                {items.map((school) => (
                  <td key={school.slug}>
                    {school.isPublic ? "publiczna" : "niepubliczna"}
                    {school.isSpecial ? ", specjalna" : ""}
                  </td>
                ))}
              </tr>
              <tr>
                <th scope="row">Typ organu prowadzącego</th>
                {items.map((school) => (
                  <td key={school.slug}>
                    {school.governingBody.typeLabel ??
                      (school.governingBody.typeCode !== null
                        ? `kod ${school.governingBody.typeCode} (opis niezweryfikowany)`
                        : "brak danych")}
                  </td>
                ))}
              </tr>
              <tr>
                <th scope="row">Wersja metodologii</th>
                {items.map((school) => (
                  <td key={school.slug}>{school.methodologyVersion}</td>
                ))}
              </tr>
            </tbody>
          </table>
        </div>
      </div>
      <p className="muted">Wynik nie jest oficjalnym rankingiem.</p>
    </>
  );
}
