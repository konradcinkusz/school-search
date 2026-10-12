import type { Metadata } from "next";
import "@/app/styles/lists.css";
import { PreschoolFilters } from "@/components/preschool-filters";
import { PreschoolList } from "@/components/preschool-list";
import { FocusOnResults } from "@/components/focus-on-results";
import { Pagination } from "@/components/pagination";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type GminaItem } from "@/lib/api";
import { formatInteger } from "@/lib/format";
import { firstParam, intParam, isTeryt, moneyParam } from "@/lib/validation";

// Data-driven: rendered per request.
export const dynamic = "force-dynamic";

const PAGE_SIZE = 20;

const description =
  "Przedszkola: lokalizacja, czesne, godziny otwarcia i wolne miejsca, każde ze źródłem i datą. Przedszkola nie mają wyniku jakości.";

export const metadata: Metadata = {
  title: "Przedszkola",
  description,
  alternates: { canonical: "/przedszkola" },
  openGraph: {
    type: "website",
    locale: "pl_PL",
    siteName: "Wyszukiwarka szkół",
    title: "Przedszkola | Wyszukiwarka szkół",
    description,
    url: "/przedszkola",
  },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function PreschoolsPage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const gminaParam = firstParam(sp.gmina, 10);
  const gmina = gminaParam && isTeryt(gminaParam) ? gminaParam : undefined;
  const q = firstParam(sp.q, 100);
  const publicOnly = firstParam(sp.publicOnly, 5) === "true";
  const hasFreePlaces = firstParam(sp.hasFreePlaces, 5) === "true";
  const maxTuitionRaw = firstParam(sp.maxTuition, 12);
  const maxTuition = moneyParam(sp.maxTuition);
  const page = intParam(sp.page, 1, 100_000);

  const [gminas, result] = await Promise.all([
    catalog.gminas(),
    catalog.preschools({
      gmina,
      q,
      publicOnly: publicOnly || undefined,
      maxTuition: maxTuition ?? undefined,
      hasFreePlaces: hasFreePlaces || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
  ]);
  const gminaList: readonly GminaItem[] = gminas.ok ? gminas.data : [];

  /** Any submitted filter field (the GET form sends them all) or a page number means a search ran. */
  const submitted = [sp.gmina, sp.q, sp.maxTuition, sp.publicOnly, sp.hasFreePlaces, sp.page].some(
    (value) => value !== undefined,
  );
  const searchKey = JSON.stringify(sp);

  const paginationParams = {
    gmina,
    q,
    publicOnly: publicOnly ? "true" : undefined,
    maxTuition: maxTuition !== null ? maxTuitionRaw : undefined,
    hasFreePlaces: hasFreePlaces ? "true" : undefined,
  };

  return (
    <section>
      <header className="page-header">
        <p className="eyebrow">Lokalizacja, czesne, wolne miejsca</p>
        <h1>Przedszkola</h1>
        <p className="lead">
          Nie porównujemy przedszkoli według jakości. Pokazujemy praktyczne informacje: lokalizację, czesne, godziny
          otwarcia i wolne miejsca. Każda wartość ma źródło i datę.
        </p>
      </header>

      <div className="card filters">
        <PreschoolFilters
          gminas={gminaList}
          values={{ gmina, q, maxTuition: maxTuitionRaw, publicOnly, hasFreePlaces }}
        />
      </div>

      {maxTuitionRaw !== undefined && maxTuition === null ? (
        <div className="notice" role="alert">
          Maksymalne czesne podaj w złotych, na przykład 600. Filtr czesnego nie został zastosowany.
        </div>
      ) : null}

      {!result.ok ? (
        result.reason === "unavailable" ? (
          <ServiceUnavailable what="listę przedszkoli" />
        ) : (
          <div className="notice" role="alert">
            Nie udało się wyszukać przedszkoli. {result.message ?? ""}
          </div>
        )
      ) : (
        <>
          <p className="result-count" id="wyniki-liczba" tabIndex={-1}>
            Znaleziono: {formatInteger(result.data.total)}
          </p>
          {submitted ? <FocusOnResults key={searchKey} targetId="wyniki-liczba" /> : null}
          <h2 className="visually-hidden">Wyniki wyszukiwania</h2>
          <PreschoolList items={result.data.items} emptyText="Brak przedszkoli spełniających te kryteria." />
          <Pagination
            page={result.data.page}
            pageSize={result.data.pageSize}
            total={result.data.total}
            basePath="/przedszkola"
            params={paginationParams}
          />
        </>
      )}
    </section>
  );
}
