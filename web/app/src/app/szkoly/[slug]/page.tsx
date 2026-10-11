import Link from "next/link";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ScoreBadge } from "@/components/score-badge";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { SchoolJsonLd } from "@/components/school-jsonld";
import { TrendChart } from "@/components/trend-chart";
import { catalog, type ScoreStatus, type SchoolProfile, type TrendPoint } from "@/lib/api";
import { asOfLabel, formatInteger, formatPercent, formatScore, formatStudents, schoolYearLabel } from "@/lib/format";
import { siteOrigin } from "@/lib/site";
import { isSlug, safeHttpUrl } from "@/lib/validation";

// Data-driven: rendered per request, never prerendered without the API.
export const dynamic = "force-dynamic";

type Props = { params: Promise<{ slug: string }> };

const SUBJECT_LABEL: Record<string, string> = {
  polish: "język polski",
  mathematics: "matematyka",
  foreign_language: "język obcy",
};

const STATUS_LABEL: Record<ScoreStatus, string> = {
  published: "wynik opublikowany",
  insufficient_cohort: "brak wyniku: zbyt mała grupa",
  no_exam_data: "brak danych egzaminacyjnych",
  no_baseline: "brak danych porównawczych",
};

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  if (!isSlug(slug)) return { title: "Szkoła" };
  const result = await catalog.school(slug);
  if (!result.ok) return { title: "Szkoła" };
  const school = result.data;
  return {
    title: school.name,
    description: `Profil szkoły ${school.name}: wynik z przedziałem, liczebność kohorty, źródła i metodologia. Nie jest oficjalnym rankingiem.`,
    alternates: { canonical: `/szkoly/${slug}` },
  };
}

export default async function SchoolPage({ params }: Props) {
  const { slug } = await params;
  if (!isSlug(slug)) notFound();

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
  const gminaLabel = school.gminaName ?? `gmina TERYT ${school.gminaTeryt}`;

  return (
    <article>
      <header>
        <h1>{school.name}</h1>
        <p className="muted">
          {gminaLabel} · {school.isPublic ? "szkoła publiczna" : "szkoła niepubliczna"}
          {school.isSpecial ? " · szkoła specjalna" : ""}
        </p>
        <p className="school-actions">
          <Link href={`/ranking/gmina:${school.gminaTeryt}`}>Ranking w gminie</Link>
          {" · "}
          <Link href={`/porownaj?s=${encodeURIComponent(school.slug)}`}>Dodaj do porównania</Link>
        </p>
      </header>

      <section aria-labelledby="result-heading">
        <h2 id="result-heading">Wynik</h2>
        <ScoreBadge
          status={school.scoreStatus}
          block={school.score}
          fallbackMethodologyVersion={school.methodologyVersion}
        />
        <p className="muted">
          Wskaźnik pokazuje, jak wynik egzaminów szkoły różni się od średniej krajowej, z uwzględnieniem udziału
          uczniów, którzy przystąpili do egzaminu. Nie jest oficjalnym rankingiem i nie ocenia nauczycieli ani
          uczniów. Zasady są w <Link href="/metodologia">metodologii</Link>.
        </p>
      </section>

      <section aria-labelledby="trend-heading">
        <h2 id="trend-heading">Wyniki w kolejnych latach</h2>
        <TrendChart points={school.trend} />
        <TrendTable trend={school.trend} />
      </section>

      <section aria-labelledby="cohorts-heading">
        <h2 id="cohorts-heading">Roczniki i oddziały</h2>
        {school.cohorts.length === 0 ? (
          <p className="muted">Brak danych o rocznikach.</p>
        ) : (
          <div className="table-wrap">
            <table>
              <caption className="muted">Rocznik szkolny, klasa, liczba uczniów i oddziałów</caption>
              <thead>
                <tr>
                  <th scope="col">Rok szkolny</th>
                  <th scope="col">Klasa</th>
                  <th scope="col">Uczniowie</th>
                  <th scope="col">Oddziały</th>
                </tr>
              </thead>
              <tbody>
                {school.cohorts.map((row) => (
                  <tr key={`${row.schoolYear}-${row.classLevel}`}>
                    <td>{schoolYearLabel(row.schoolYear)}</td>
                    <td>{row.classLevel}</td>
                    <td>{formatInteger(row.pupils)}</td>
                    <td>{formatInteger(row.oddzialy)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section aria-labelledby="body-heading">
        <h2 id="body-heading">Organ prowadzący</h2>
        <GoverningBodyNote school={school} />
      </section>

      <section aria-labelledby="faith-heading">
        <h2 id="faith-heading">Charakter wyznaniowy</h2>
        <p>
          {school.faith.derived
            ? "Ustalone pośrednio: wyprowadzone z organu prowadzącego, nie z nazwy szkoły."
            : "Nie wyprowadzono z nazwy szkoły."}{" "}
          {school.faith.declared === true
            ? "Szkoła deklaruje charakter wyznaniowy."
            : school.faith.declared === false
              ? "Szkoła deklaruje brak charakteru wyznaniowego."
              : "Brak deklaracji szkoły."}
        </p>
        {school.faith.note ? <p className="muted">{school.faith.note}</p> : null}
      </section>

      <section aria-labelledby="provenance-heading">
        <h2 id="provenance-heading">Źródła i stan danych</h2>
        <ProvenanceTable rows={school.provenance} />
        <p className="muted">Wersja metodologii: {school.methodologyVersion}</p>
      </section>

      <p className="muted">
        Widzisz błąd w danych tej szkoły?{" "}
        <Link href={`/dla-szkol?slug=${encodeURIComponent(school.slug)}`}>Zgłoś błąd w danych</Link>.
      </p>

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
      <table>
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
              <td>{row.examYear}</td>
              <td>{STATUS_LABEL[row.scoreStatus]}</td>
              <td>
                {row.score && row.score.score !== null ? (
                  <>
                    {formatScore(row.score.score)}
                    {row.score.intervalLow !== null && row.score.intervalHigh !== null ? (
                      <span className="muted">
                        {" "}
                        (od {formatScore(row.score.intervalLow)} do {formatScore(row.score.intervalHigh)})
                      </span>
                    ) : null}
                    <br />
                    <span className="muted">{formatStudents(row.score.cohortSize)}</span>
                  </>
                ) : (
                  <span className="muted">brak wyniku</span>
                )}
              </td>
              <td>{row.coveragePercent !== null ? formatPercent(row.coveragePercent) : "brak danych"}</td>
              <td>
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
      <table>
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
            return (
              <tr key={`${row.field}-${index}`}>
                <td>{row.field}</td>
                <td>
                  {url ? (
                    <a href={url} rel="noopener noreferrer nofollow">
                      {row.source}
                    </a>
                  ) : (
                    row.source
                  )}
                </td>
                <td>{asOfLabel(row.asOf)}</td>
                <td>{row.method}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
