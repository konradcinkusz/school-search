import Link from "next/link";
import { catalog, type CatchmentResponse, type GminaItem, type SchoolListItem } from "@/lib/api";
import { CoverageBadge } from "@/components/coverage-badge";
import { SchoolList } from "@/components/school-list";
import { ServiceUnavailable } from "@/components/service-unavailable";

/** Radius for "schools in reach" around the catchment school, in km (API maximum is 50). */
const REACH_RADIUS_KM = 3;
const REACH_PAGE_SIZE = 20;

export interface CatchmentPanelProps {
  readonly gmina: GminaItem;
  readonly street: string;
  readonly number: string | undefined;
}

function addressLine(street: string, number: string | undefined): string {
  return number ? `${street} ${number}` : street;
}

/** Catchment result for one address, then schools in reach. Never synthesises a boundary from distance. */
export async function CatchmentPanel({ gmina, street, number }: CatchmentPanelProps) {
  const result = await catalog.catchment({ gmina: gmina.teryt, street, number });
  if (!result.ok) {
    return result.reason === "unavailable" ? (
      <ServiceUnavailable what="obwód szkolny dla tego adresu" />
    ) : (
      <div className="notice" role="alert">
        Nie udało się sprawdzić tego adresu. {result.message ?? ""}
      </div>
    );
  }
  const data: CatchmentResponse = result.data;
  const catchmentSchools: readonly SchoolListItem[] =
    data.match === "street_list_match" ? data.schools : [];
  const anchor = catchmentSchools.find((school) => school.latitude !== null && school.longitude !== null);
  const catchmentSlugs = new Set(catchmentSchools.map((school) => school.slug));

  return (
    <section className="card catchment" aria-labelledby="catchment-heading" aria-live="polite">
      <p className="eyebrow">Sprawdzony adres</p>
      <h2 id="catchment-heading">Obwód szkolny</h2>
      <p className="catchment__address">
        Adres: <strong>{addressLine(street, number)}</strong>, {gmina.name ?? "Nieznana gmina"}
        {gmina.name === null ? <span className="muted"> (kod {gmina.teryt})</span> : null}
      </p>

      <CoverageBadge
        state={data.coverage.state}
        resolutionUrl={data.coverage.resolutionUrl}
        resolutionDate={data.coverage.resolutionDate}
      />

      {data.match === "street_list_match" ? (
        <>
          <h3>Szkoła obwodowa</h3>
          <div className="catchment__school">
            <SchoolList items={catchmentSchools} emptyText="Brak szkoły przypisanej do tej ulicy w danych." />
          </div>
        </>
      ) : null}
      {data.match === "no_match" ? (
        <div className="notice notice--caution" role="note">
          Nie znaleźliśmy tej ulicy na liście obwodów tej gminy. Sprawdź pisownię nazwy ulicy albo uchwałę gminy.
        </div>
      ) : null}
      {data.match === "no_data" ? (
        <div className="notice notice--caution" role="note">
          Ta gmina nie ma w serwisie danych o obwodach. Obwód ustala gmina. Nie wyznaczamy go na podstawie odległości.
        </div>
      ) : null}

      <p className="muted">{data.disclaimer}</p>

      <h3>Szkoły w zasięgu</h3>
      <InReach
        gmina={gmina}
        anchor={data.match === "street_list_match" ? anchor ?? null : null}
        excludeSlugs={catchmentSlugs}
      />
      <p className="actions">
        <Link href={`/ranking/gmina:${gmina.teryt}`} className="button button--secondary">
          Ranking szkół w tej gminie
        </Link>
      </p>
    </section>
  );
}

async function InReach({
  gmina,
  anchor,
  excludeSlugs,
}: {
  gmina: GminaItem;
  anchor: SchoolListItem | null;
  excludeSlugs: ReadonlySet<string>;
}) {
  if (anchor && anchor.latitude !== null && anchor.longitude !== null) {
    const near = await catalog.schools({
      lat: anchor.latitude,
      lon: anchor.longitude,
      radiusKm: REACH_RADIUS_KM,
      pageSize: REACH_PAGE_SIZE,
    });
    if (!near.ok) {
      return <ServiceUnavailable what="szkoły w zasięgu" />;
    }
    const items = near.data.items.filter((school) => !excludeSlugs.has(school.slug));
    return (
      <>
        <p className="muted">
          Szkoły w promieniu {REACH_RADIUS_KM}&nbsp;km od szkoły obwodowej. Zasięg nie oznacza, że przyjęcie jest
          pewne.
        </p>
        <SchoolList items={items} emptyText="Nie ma innych szkół w tym promieniu." />
      </>
    );
  }

  const inGmina = await catalog.schools({ gmina: gmina.teryt, pageSize: REACH_PAGE_SIZE });
  if (!inGmina.ok) {
    return <ServiceUnavailable what="szkoły w gminie" />;
  }
  const items = inGmina.data.items.filter((school) => !excludeSlugs.has(school.slug));
  return (
    <>
      <p className="muted">
        Brak współrzędnych szkoły obwodowej, więc pokazujemy szkoły w gminie (pierwsze {REACH_PAGE_SIZE} z{" "}
        {inGmina.data.total}). To nie jest lista szkół obwodowych.
      </p>
      <SchoolList items={items} emptyText="Brak szkół w tej gminie." />
    </>
  );
}
