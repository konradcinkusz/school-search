import type { Metadata } from "next";
import "@/app/styles/lists.css";
import "@/app/styles/map.css";
import { MapExplorer } from "@/components/map/map-explorer";
import { filtersFromSearchParams } from "@/lib/search-filters";

// Data-driven: filters come from the URL, markers are fetched in the browser through /api/map/markers.
export const dynamic = "force-dynamic";

const description =
  "Mapa szkół podstawowych i przedszkoli: wybierz okolicę i filtry. Znaczniki są neutralne i nie oznaczają jakości placówki.";

export const metadata: Metadata = {
  title: "Mapa szkół i przedszkoli",
  description,
  alternates: { canonical: "/mapa" },
  openGraph: {
    type: "website",
    locale: "pl_PL",
    siteName: "Wyszukiwarka szkół",
    title: "Mapa szkół i przedszkoli | Wyszukiwarka szkół",
    description,
    url: "/mapa",
  },
};

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export default async function MapPage({ searchParams }: { searchParams: SearchParams }) {
  const filters = filtersFromSearchParams(await searchParams);
  return (
    <section>
      <header className="page-header">
        <p className="eyebrow">Okolica, rodzaj placówki, filtry</p>
        <h1>Mapa szkół i przedszkoli</h1>
        <p className="lead">
          Przeglądaj placówki w okolicy. Wszystkie znaczniki wyglądają tak samo dla danego rodzaju placówki: kolor i
          kształt nie oznaczają jakości, wyniku ani miejsca w rankingu.
        </p>
      </header>
      <MapExplorer initialFilters={filters} />
    </section>
  );
}
