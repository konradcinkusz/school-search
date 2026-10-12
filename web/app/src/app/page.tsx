import Link from "next/link";
import type { Metadata } from "next";
import { AddressForm } from "@/components/address-form";
import { SearchIntentBox } from "@/components/ai/search-intent-box";
import { CatchmentPanel } from "@/components/catchment-panel";
import { Pagination } from "@/components/pagination";
import { SchoolList } from "@/components/school-list";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type GminaItem } from "@/lib/api";
import { firstParam, intParam, isTeryt } from "@/lib/validation";

// Data-driven: never prerendered at build time, when the API is not reachable.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: { absolute: "Start | Wyszukiwarka szkół" },
  description:
    "Wpisz adres, aby zobaczyć obwód szkolny i szkoły w zasięgu wraz z wynikami, ich niepewnością i liczebnością kohort. Nie jest oficjalnym rankingiem.",
};

const NAME_QUERY_MIN = 2;
const NAME_PAGE_SIZE = 20;

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function HomePage({ searchParams }: { searchParams: SearchParams }) {
  const sp = await searchParams;
  const gminaParam = firstParam(sp.gmina, 10);
  const street = firstParam(sp.ulica, 200);
  const number = firstParam(sp.numer, 20);
  const nameQuery = firstParam(sp.q, 100);
  const page = intParam(sp.page, 1, 1000);

  const gminas = await catalog.gminas();
  const gminaList: readonly GminaItem[] = gminas.ok ? gminas.data : [];
  const selectedGmina =
    gminaParam && isTeryt(gminaParam) ? gminaList.find((gmina) => gmina.teryt === gminaParam) : undefined;

  const hasLookup = selectedGmina !== undefined && street !== undefined && street !== "";

  return (
    <div>
      <section className="hero" aria-labelledby="home-heading">
        <div className="hero__inner">
          <div className="hero__intro">
            <p className="eyebrow">Niezależna wyszukiwarka szkół podstawowych</p>
            <h1 id="home-heading">Znajdź szkołę podstawową dla swojego adresu</h1>
            <p className="hero__lead">
              Wpisz adres, a zobaczysz obwód szkolny oraz szkoły w zasięgu, z wynikami, ich niepewnością i liczebnością
              kohort.
            </p>
          </div>

          <div className="hero__panel">
            <h2>Twój adres</h2>
            {gminas.ok ? (
              <AddressForm gminas={gminaList} values={{ gmina: selectedGmina?.teryt, street, number }} />
            ) : (
              <ServiceUnavailable what="listę gmin" />
            )}

            {gminas.ok && gminaParam && !selectedGmina ? (
              <div className="notice" role="alert">
                Wybierz gminę z listy.
              </div>
            ) : null}
            {selectedGmina && !street ? (
              <div className="notice" role="note">
                Podaj nazwę ulicy, aby sprawdzić obwód.
              </div>
            ) : null}
          </div>

          {hasLookup ? null : <Principles />}
        </div>
      </section>

      {selectedGmina && street ? <CatchmentPanel gmina={selectedGmina} street={street} number={number} /> : null}

      <div className="home-grid">
        <SearchIntentBox />

        <section aria-labelledby="name-search-heading" className="card name-search">
          <h2 id="name-search-heading">Szukaj szkoły po nazwie</h2>
          <form method="get" action="/" className="name-search__form inline-form" role="search">
            <div className="field">
              <label htmlFor="q">Nazwa szkoły</label>
              <input
                id="q"
                name="q"
                type="search"
                minLength={NAME_QUERY_MIN}
                maxLength={100}
                defaultValue={nameQuery ?? ""}
              />
            </div>
            <button type="submit" className="button">
              Szukaj
            </button>
          </form>
        </section>
      </div>

      <NameResults query={nameQuery} page={page} />

      <p className="muted home-footnote">
        Każdy wynik jest opisany w <Link href="/metodologia">metodologii</Link>. Nie jest oficjalnym rankingiem.
      </p>
    </div>
  );
}

/** Three plain statements of how values are shown. Icons are decorative. */
function Principles() {
  return (
    <ul className="principles" aria-label="Jak pokazujemy dane">
      <li>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
          <path d="M4 12h16M8 8v8M16 8v8" strokeLinecap="round" />
        </svg>
        <span>
          <strong>Wynik zawsze z niepewnością</strong>
          Przedział 95%, liczebność kohorty i wersja metodologii są przy każdym wyniku.
        </span>
      </li>
      <li>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
          <path d="M6 4h9l3 3v13H6z" strokeLinejoin="round" />
          <path d="M9 11h6M9 15h6" strokeLinecap="round" />
        </svg>
        <span>
          <strong>Źródło przy każdej wartości</strong>
          Podajemy źródło, datę stanu danych i metodę.
        </span>
      </li>
      <li>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" focusable="false">
          <circle cx="12" cy="12" r="8" />
          <path d="M12 8v5M12 16v.01" strokeLinecap="round" />
        </svg>
        <span>
          <strong>Brak wyniku to nie zero</strong>
          Gdy grupa uczniów jest zbyt mała, mówimy to wprost zamiast pokazywać liczbę.
        </span>
      </li>
    </ul>
  );
}

async function NameResults({ query, page }: { query: string | undefined; page: number }) {
  if (query === undefined) return null;
  if (query.length < NAME_QUERY_MIN) {
    return (
      <p className="notice" role="note">
        Wpisz co najmniej {NAME_QUERY_MIN} znaki.
      </p>
    );
  }
  const result = await catalog.schools({ q: query, page, pageSize: NAME_PAGE_SIZE });
  if (!result.ok) {
    return <ServiceUnavailable what="wyniki wyszukiwania" />;
  }
  return (
    <section aria-labelledby="name-results-heading" className="section">
      <h2 id="name-results-heading">Wyniki wyszukiwania</h2>
      <p className="result-count">Znaleziono: {result.data.total}</p>
      <SchoolList items={result.data.items} emptyText="Nie znaleziono szkół o tej nazwie." />
      <Pagination
        page={result.data.page}
        pageSize={result.data.pageSize}
        total={result.data.total}
        basePath="/"
        params={{ q: query }}
      />
    </section>
  );
}
