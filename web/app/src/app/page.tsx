import Link from "next/link";
import type { Metadata } from "next";
import { AddressForm } from "@/components/address-form";
import { CatchmentPanel } from "@/components/catchment-panel";
import { Pagination } from "@/components/pagination";
import { SchoolList } from "@/components/school-list";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type GminaItem } from "@/lib/api";
import { firstParam, intParam, isTeryt } from "@/lib/validation";

// Data-driven: never prerendered at build time, when the API is not reachable.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Start",
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

  return (
    <div>
      <section>
        <h1>Znajdź szkołę podstawową dla swojego adresu</h1>
        <p>
          Wpisz adres, a zobaczysz obwód szkolny oraz szkoły w zasięgu, z wynikami, ich niepewnością i liczebnością
          kohort.
        </p>
        {gminas.ok ? (
          <AddressForm
            gminas={gminaList}
            values={{ gmina: selectedGmina?.teryt, street, number }}
          />
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
        {selectedGmina && street ? (
          <CatchmentPanel gmina={selectedGmina} street={street} number={number} />
        ) : null}
      </section>

      <section aria-labelledby="name-search-heading" className="name-search">
        <h2 id="name-search-heading">Szukaj szkoły po nazwie</h2>
        <form method="get" action="/" className="name-search__form">
          <div className="field">
            <label htmlFor="q">Nazwa szkoły</label>
            <input id="q" name="q" type="search" minLength={NAME_QUERY_MIN} maxLength={100} defaultValue={nameQuery ?? ""} />
          </div>
          <button type="submit" className="button">
            Szukaj
          </button>
        </form>
        <NameResults query={nameQuery} page={page} />
      </section>

      <p className="muted">
        Każdy wynik jest opisany w <Link href="/metodologia">metodologii</Link>. Nie jest oficjalnym rankingiem.
      </p>
    </div>
  );
}

async function NameResults({ query, page }: { query: string | undefined; page: number }) {
  if (query === undefined) return null;
  if (query.length < NAME_QUERY_MIN) {
    return <p className="muted">Wpisz co najmniej {NAME_QUERY_MIN} znaki.</p>;
  }
  const result = await catalog.schools({ q: query, page, pageSize: NAME_PAGE_SIZE });
  if (!result.ok) {
    return <ServiceUnavailable what="wyniki wyszukiwania" />;
  }
  return (
    <>
      <p className="muted">Znaleziono: {result.data.total}</p>
      <SchoolList items={result.data.items} emptyText="Nie znaleziono szkół o tej nazwie." />
      <Pagination
        page={result.data.page}
        pageSize={result.data.pageSize}
        total={result.data.total}
        basePath="/"
        params={{ q: query }}
      />
    </>
  );
}
