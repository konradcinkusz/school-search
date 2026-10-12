import Link from "next/link";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ExplainPanel } from "@/components/explain-panel";
import { SponsoredProfile } from "@/components/billing/sponsored-profile";
import { ReviewsSection } from "@/components/reviews/reviews-section";
import { ScoreBadge } from "@/components/score-badge";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { SchoolJsonLd } from "@/components/school-jsonld";
import { TrendChart } from "@/components/trend-chart";
import { catalog, type ScoreStatus, type SchoolProfile, type TrendPoint } from "@/lib/api";
import { asOfLabel, formatInteger, formatPercent, formatScore, formatStudents, schoolYearLabel } from "@/lib/format";
import { siteOrigin } from "@/lib/site";
import { intParam, isSlug, safeHttpUrl } from "@/lib/validation";
import "../../styles/profile.css";

// Data-driven: rendered per request, never prerendered without the API.
export const dynamic = "force-dynamic";

type SearchParams = Promise<Record<string, string | string[] | undefined>>;
type Props = { params: Promise<{ slug: string }>; searchParams: SearchParams };

const SUBJECT_LABEL: Record<string, string> = {
  polish: "język polski",
  mathematics: "matematyka",
  foreign_language: "język obcy",
};

/** Source marker of the synthetic demo seed (never in Production). Rows carrying it are labelled as demo data. */
const DEMO_MARK = "DEMO SEED";

/** Polish names for the provenance fields the API emits (SchoolReadModel.Provenance). Unknown keys fall back to a neutral label. */
const PROVENANCE_FIELD_LABEL: Record<string, string> = {
  score: "Wynik",
  cohorts: "Roczniki i oddziały",
  examResults: "Wyniki egzaminów",
  coordinates: "Współrzędne",
  name: "Nazwa szkoły",
  gmina: "Gmina",
};

/** Method sentences for the provenance methods the importers write. Prefix matches cover methods with a suffix. */
const PROVENANCE_METHOD_LABEL: readonly { readonly match: string; readonly label: string }[] = [
  { match: "synthetic", label: "Dane syntetyczne (demonstracyjne), nie z urzędowego źródła" },
  { match: "csv import", label: "Import z pliku CSV" },
  { match: "xlsx import", label: "Import z pliku XLSX" },
  { match: "importer run record", label: "Zapis z przebiegu importu" },
  { match: "llm-extract", label: "Wyodrębnione automatycznie, bez weryfikacji przez człowieka" },
  { match: "code seen in SIO file", label: "Kod z pliku SIO, jeszcze niezweryfikowany" },
  { match: "seed from product brief", label: "Ustalone z opisu produktu, lista kodów niezweryfikowana" },
];
const PROVENANCE_METHOD_FALLBACK = "Metoda opisana w metodologii";

function provenanceFieldLabel(field: string): string {
  return PROVENANCE_FIELD_LABEL[field] ?? "Inne dane";
}

function provenanceMethodLabel(method: string): string {
  const normalised = method.trim().toLowerCase();
  const hit = PROVENANCE_METHOD_LABEL.find((entry) => normalised.startsWith(entry.match));
  return hit ? hit.label : PROVENANCE_METHOD_FALLBACK;
}

const STATUS_LABEL: Record<ScoreStatus, string> = {
  published: "wynik opublikowany",
  limited: "profil ograniczony: mała grupa, bez wyniku i miejsca",
  insufficient_cohort: "brak wyniku: zbyt mała grupa",
  no_exam_data: "nie ma danych egzaminacyjnych",
  no_baseline: "brak danych porównawczych",
};

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  if (!isSlug(slug)) return { title: "Nie znaleziono strony", robots: { index: false } };
  const result = await catalog.school(slug);
  if (!result.ok) {
    if (result.reason === "not_found") return { title: "Nie znaleziono strony", robots: { index: false } };
    return { title: "Szkoła" };
  }
  const school = result.data;
  const description = `Profil szkoły ${school.name}: wynik z przedziałem, liczba uczniów w grupie porównawczej, źródła i metodologia. Nie jest oficjalnym rankingiem.`;
  return {
    title: school.name,
    description,
    alternates: { canonical: `/szkoly/${slug}` },
    openGraph: {
      type: "website",
      locale: "pl_PL",
      siteName: "Wyszukiwarka szkół",
      title: `${school.name} | Wyszukiwarka szkół`,
      description,
      url: `/szkoly/${slug}`,
    },
  };
}

export default async function SchoolPage({ params, searchParams }: Props) {
  const { slug } = await params;
  if (!isSlug(slug)) notFound();
  const sp = await searchParams;
  const reviewsPage = intParam(sp.opinie, 1, 100_000);

  const result = await catalog.school(slug);
  if (!result.ok) {
    if (result.reason === "not_found") notFound();
    return (
      <article>
        <h1>Szkoła</h1>
        <ServiceUnavailable what="profil szkoły" />
      </article>
    );
  }
  const school = result.data;
  const gminaLabel = school.gminaName ?? "Nieznana gmina";
  const isDemo = school.provenance.some((row) => row.source.includes(DEMO_MARK));
  const scoreRow = school.provenance.find((row) => row.field === "score");
  const scoreProvenance = scoreRow ? { source: scoreRow.source, asOf: scoreRow.asOf } : null;

  return (
    <article>
      <header className="profile-header">
        <p className="eyebrow">Profil szkoły</p>
        <h1>{school.name}</h1>
        <ul className="meta-row" aria-label="Podstawowe informacje">
          <li>
            <span className="badge">{gminaLabel}</span>
          </li>
          <li>
            <span className="badge">{school.isPublic ? "szkoła publiczna" : "szkoła niepubliczna"}</span>
          </li>
          {school.isSpecial ? (
            <li>
              <span className="badge">szkoła specjalna</span>
            </li>
          ) : null}
        </ul>
        {isDemo ? (
          <p className="notice notice--demo" role="note">
            <strong>Dane demonstracyjne ({DEMO_MARK}, syntetyczne).</strong> Ta szkoła i wszystkie jej wartości są
            fikcyjne. Służą tylko do pokazania działania serwisu.
          </p>
        ) : null}
        <p className="school-actions">
          <Link href={`/ranking/gmina:${school.gminaTeryt}`} className="button button--secondary">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true" focusable="false">
              <path d="M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01" />
            </svg>
            Ranking w gminie
          </Link>
          <Link href={`/porownaj?s=${encodeURIComponent(school.slug)}`} className="button button--secondary">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
              <rect x="3.5" y="5" width="7" height="14" rx="1.5" />
              <rect x="13.5" y="5" width="7" height="14" rx="1.5" />
            </svg>
            Dodaj do porównania
          </Link>
        </p>
      </header>

      {/* One column: the result is the only card in its row. The AI explanation sits below it, full width. */}
      <div className="profile-grid profile-grid--result">
        <section aria-labelledby="result-heading" className="card result-card">
          <h2 id="result-heading">Wynik</h2>
          <ScoreBadge
            status={school.scoreStatus}
            block={school.score}
            fallbackMethodologyVersion={school.methodologyVersion}
            provenance={scoreProvenance}
            variant="hero"
          />
          {school.scoreStatus === "published" ? (
            <p className="score-explainer">
              Wskaźnik pokazuje, jak wynik egzaminów szkoły różni się od średniej krajowej, z uwzględnieniem udziału
              uczniów, którzy przystąpili do egzaminu. Nie jest oficjalnym rankingiem i nie ocenia nauczycieli ani
              uczniów. Zasady są w <Link href="/metodologia">metodologii</Link>.
            </p>
          ) : (
            <p className="score-explainer">
              Wynik jest liczony tylko wtedy, gdy w egzaminach przystąpiło wystarczająco dużo uczniów. Zasady są w{" "}
              <Link href="/metodologia">metodologii</Link>.
            </p>
          )}
        </section>

        {school.scoreStatus === "published" ? <ExplainPanel slug={school.slug} /> : null}
      </div>

      <section aria-labelledby="trend-heading" className="section">
        <h2 id="trend-heading">Wyniki w kolejnych latach</h2>
        <TrendChart points={school.trend} />
        <TrendTable trend={school.trend} />
        {school.scoreStatus === "published" ? null : (
          <p className="muted explain-off">Wyjaśnienie AI jest dostępne, gdy szkoła ma opublikowany wynik.</p>
        )}
      </section>

      <section aria-labelledby="cohorts-heading" className="section">
        <h2 id="cohorts-heading">Roczniki i oddziały</h2>
        {school.cohorts.length === 0 ? (
          <p className="muted">Brak danych o rocznikach.</p>
        ) : (
          <div className="table-wrap">
            <table className="table--stack">
              <caption className="muted">Rocznik szkolny, klasa, liczba uczniów i oddziałów</caption>
              <thead>
                <tr>
                  <th scope="col">Rok szkolny</th>
                  <th scope="col">Klasa</th>
                  <th scope="col" className="num">
                    Uczniowie
                  </th>
                  <th scope="col" className="num">
                    Oddziały
                  </th>
                </tr>
              </thead>
              <tbody>
                {school.cohorts.map((row) => (
                  <tr key={`${row.schoolYear}-${row.classLevel}`}>
                    <td data-label="Rok szkolny">{schoolYearLabel(row.schoolYear)}</td>
                    <td data-label="Klasa">{row.classLevel}</td>
                    <td className="num" data-label="Uczniowie">
                      {formatInteger(row.pupils)}
                    </td>
                    <td className="num" data-label="Oddziały">
                      {formatInteger(row.oddzialy)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <div className="profile-grid section-pair">
        <section aria-labelledby="body-heading" className="card">
          <h2 id="body-heading">Organ prowadzący</h2>
          <GoverningBodyNote school={school} />
        </section>

        <section aria-labelledby="faith-heading" className="card">
          <h2 id="faith-heading">Charakter wyznaniowy</h2>
          <p>
            {school.faith.derived ? "Ustalone pośrednio na podstawie organu prowadzącego, nie nazwy szkoły. " : null}
            {school.faith.declared === true
              ? "Szkoła deklaruje charakter wyznaniowy."
              : school.faith.declared === false
                ? "Szkoła deklaruje brak charakteru wyznaniowego."
                : school.faith.derived
                  ? null
                  : "Szkoła nie zadeklarowała charakteru wyznaniowego."}
          </p>
          {school.faith.note ? <p className="muted">{school.faith.note}</p> : null}
        </section>
      </div>

      <section aria-labelledby="provenance-heading" className="section">
        <h2 id="provenance-heading">Źródła i stan danych</h2>
        <ProvenanceTable rows={school.provenance} />
        <p className="muted">Wersja metodologii: {school.methodologyVersion}</p>
      </section>

      <ReviewsSection slug={school.slug} page={reviewsPage} />

      <SponsoredProfile slug={school.slug} />

      <div className="notice correction-cta" role="note">
        <p>
          <strong>Widzisz błąd w danych tej szkoły?</strong>
        </p>
        <p className="actions">
          <Link className="button button--secondary" href={`/dla-szkol?slug=${encodeURIComponent(school.slug)}`}>
            Zgłoś błąd w danych
          </Link>
        </p>
      </div>

      <SchoolJsonLd school={{ name: school.name, url: `${siteOrigin()}/szkoly/${school.slug}` }} />
    </article>
  );
}

function GoverningBodyNote({ school }: { school: SchoolProfile }) {
  const body = school.governingBody;
  return (
    <div>
      <p>
        Typ organu prowadzącego: <strong>{body.typeLabel ?? "nieznany"}</strong>
        {body.typeCode !== null ? <span className="muted"> (kod {body.typeCode})</span> : null}
      </p>
      {!body.labelVerified ? (
        <p className="muted">Etykieta typu nie została jeszcze zweryfikowana.</p>
      ) : null}
      {body.isNaturalPerson ? (
        <p className="muted">Organ prowadzący jest osobą fizyczną. Nazwa podmiotu nie jest publikowana.</p>
      ) : null}
    </div>
  );
}

function TrendTable({ trend }: { trend: readonly TrendPoint[] }) {
  if (trend.length === 0) {
    return <p className="muted">Brak danych egzaminacyjnych dla tej szkoły.</p>;
  }
  const rows = [...trend].sort((a, b) => a.examYear - b.examYear);
  return (
    <div className="table-wrap">
      <table className="table--stack trend-table">
        <caption className="muted">Wyniki i statusy według roku egzaminu</caption>
        <thead>
          <tr>
            <th scope="col">Rok egzaminu</th>
            <th scope="col">Status</th>
            <th scope="col">Wynik</th>
            <th scope="col">Pokrycie</th>
            <th scope="col">Średnie wyniki z przedmiotów</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.examYear}>
              <td data-label="Rok egzaminu">
                <strong>{row.examYear}</strong>
              </td>
              <td data-label="Status">{STATUS_LABEL[row.scoreStatus]}</td>
              <td data-label="Wynik">
                {row.score && row.score.score !== null ? (
                  <>
                    {formatScore(row.score.score)}
                    {row.score.intervalLow !== null && row.score.intervalHigh !== null ? (
                      <span className="muted trend__interval">
                        {" "}
                        (od {formatScore(row.score.intervalLow)} do {formatScore(row.score.intervalHigh)})
                      </span>
                    ) : null}
                    <br />
                    <span className="muted">{formatStudents(row.score.cohortSize)}</span>
                  </>
                ) : (
                  <span className="muted">nie dotyczy</span>
                )}
              </td>
              <td data-label="Pokrycie">
                {row.coveragePercent !== null ? formatPercent(row.coveragePercent) : "brak danych"}
              </td>
              <td data-label="Średnie wyniki z przedmiotów">
                {row.subjects.length === 0 ? (
                  <span className="muted">brak danych</span>
                ) : (
                  <ul className="plain-list">
                    {row.subjects.map((subject) => (
                      <li key={subject.subject}>
                        {SUBJECT_LABEL[subject.subject] ?? subject.subject}:{" "}
                        {subject.meanPercent !== null ? formatPercent(subject.meanPercent) : "brak"}{" "}
                        <span className="muted">({formatStudents(subject.pupilsSat)} przystąpiło)</span>
                      </li>
                    ))}
                  </ul>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProvenanceTable({ rows }: { rows: SchoolProfile["provenance"] }) {
  if (rows.length === 0) {
    return <p className="muted">Brak zapisanych źródeł dla tej szkoły.</p>;
  }
  return (
    <div className="table-wrap">
      <table className="table--stack">
        <caption className="muted">Każda wartość ma źródło, datę i metodę</caption>
        <thead>
          <tr>
            <th scope="col">Pole</th>
            <th scope="col">Źródło</th>
            <th scope="col">Stan</th>
            <th scope="col">Metoda</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => {
            const url = safeHttpUrl(row.sourceUrl);
            const demo = row.source.includes(DEMO_MARK);
            const source = url ? (
              <a href={url} rel="noopener noreferrer nofollow">
                {row.source}
              </a>
            ) : (
              row.source
            );
            return (
              <tr key={`${row.field}-${index}`} data-demo={demo ? "true" : undefined}>
                <td data-label="Pole">
                  <strong>{provenanceFieldLabel(row.field)}</strong>
                </td>
                <td data-label="Źródło">{source}</td>
                <td data-label="Stan">{asOfLabel(row.asOf)}</td>
                <td data-label="Metoda">{provenanceMethodLabel(row.method)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
