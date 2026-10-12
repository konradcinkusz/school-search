import Link from "next/link";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { PreschoolJsonLd } from "@/components/preschool-jsonld";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { catalog, type GoverningBody, type PreschoolFactsBlock } from "@/lib/api";
import { asOfLabel } from "@/lib/format";
import {
  confidenceLabel,
  formatPln,
  freePlacesLabel,
  hoursLabel,
  tuitionLabel,
} from "@/lib/preschool";
import { siteOrigin } from "@/lib/site";
import { isSlug, safeHttpUrl } from "@/lib/validation";

// Data-driven: rendered per request, never prerendered without the API.
export const dynamic = "force-dynamic";

const UNKNOWN = "brak danych";

type Props = { params: Promise<{ slug: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  if (!isSlug(slug)) return { title: "Przedszkole" };
  const result = await catalog.preschool(slug);
  if (!result.ok) return { title: "Przedszkole" };
  const preschool = result.data;
  return {
    title: preschool.name,
    description: `Przedszkole ${preschool.name}: lokalizacja, czesne, godziny otwarcia i wolne miejsca, każde ze źródłem i datą.`,
    alternates: { canonical: `/przedszkola/${slug}` },
  };
}

export default async function PreschoolPage({ params }: Props) {
  const { slug } = await params;
  if (!isSlug(slug)) notFound();

  const result = await catalog.preschool(slug);
  if (!result.ok) {
    if (result.reason === "not_found") notFound();
    return (
      <article>
        <h1>Przedszkole</h1>
        <ServiceUnavailable what="profil przedszkola" />
      </article>
    );
  }
  const preschool = result.data;
  const gminaLabel = preschool.gminaName ?? (preschool.gminaTeryt ? `gmina TERYT ${preschool.gminaTeryt}` : null);
  const address = [preschool.street, [preschool.postalCode, preschool.city].filter(Boolean).join(" ")]
    .filter((part) => part)
    .join(", ");

  return (
    <article>
      <header className="profile-header">
        <p className="eyebrow">Profil przedszkola</p>
        <h1>{preschool.name}</h1>
        <ul className="meta-row" aria-label="Podstawowe informacje">
          <li>
            <span className="badge">{gminaLabel ?? "Gmina: brak danych"}</span>
          </li>
          <li>
            <span className="badge">{preschool.isPublic ? "przedszkole publiczne" : "przedszkole niepubliczne"}</span>
          </li>
        </ul>
        {preschool.gminaTeryt ? (
          <p className="school-actions">
            <Link
              href={`/przedszkola?gmina=${encodeURIComponent(preschool.gminaTeryt)}`}
              className="button button--secondary"
            >
              Przedszkola w tej gminie
            </Link>
            <Link href="/przedszkola" className="button button--secondary">
              Wszystkie przedszkola
            </Link>
          </p>
        ) : null}
      </header>

      <div className="notice notice--caution" role="note">
        {preschool.notice}
      </div>

      <section aria-labelledby="address-heading" className="section">
        <h2 id="address-heading">Adres</h2>
        <p>{address !== "" ? address : <span className="muted">{UNKNOWN}</span>}</p>
      </section>

      <section aria-labelledby="facts-heading" className="section">
        <h2 id="facts-heading">Opłaty, godziny i wolne miejsca</h2>
        <FactsTable facts={preschool.facts} />
      </section>

      <section aria-labelledby="body-heading" className="section">
        <h2 id="body-heading">Organ prowadzący</h2>
        <GoverningBodyNote body={preschool.governingBody} />
      </section>

      <section aria-labelledby="corrections-heading" className="section">
        <h2 id="corrections-heading">Korekty danych</h2>
        <p className="muted">
          Formularz korekt jest obecnie dla szkół podstawowych. Korekty danych przedszkoli nie są jeszcze przyjmowane
          tym formularzem. Każda wartość na tej stronie ma źródło i datę, dzięki czemu można ją sprawdzić.
        </p>
      </section>

      <PreschoolJsonLd
        preschool={{
          name: preschool.name,
          url: `${siteOrigin()}/przedszkola/${preschool.slug}`,
          street: preschool.street,
          postalCode: preschool.postalCode,
          city: preschool.city,
        }}
      />
    </article>
  );
}

/** One row per fact. A null value shows "brak danych"; its source, date and confidence are not shown. */
function FactsTable({ facts }: { facts: PreschoolFactsBlock | null }) {
  const source = facts?.source ?? null;
  const tuition = facts ? tuitionLabel(facts.monthlyTuition) : null;
  const meal = facts?.monthlyMealPln ?? null;
  const hours = facts ? hoursLabel(facts.hours) : null;
  const places = facts ? freePlacesLabel(facts.freePlaces.count) : null;

  const rows: ReadonlyArray<{
    readonly key: string;
    readonly label: string;
    readonly value: string | null;
    readonly asOf: string | null;
  }> = [
    { key: "tuition", label: "Czesne", value: tuition, asOf: source?.asOf ?? null },
    {
      key: "meal",
      label: "Wyżywienie (miesięcznie)",
      value: meal !== null ? formatPln(meal) : null,
      asOf: source?.asOf ?? null,
    },
    { key: "hours", label: "Godziny otwarcia", value: hours, asOf: source?.asOf ?? null },
    { key: "places", label: "Wolne miejsca", value: places, asOf: facts?.freePlaces.asOf ?? null },
  ];

  if (facts === null) {
    return <p className="muted">Brak zapisanych danych o opłatach, godzinach i wolnych miejscach.</p>;
  }

  return (
    <div className="table-wrap">
      <table className="table--stack">
        <caption className="muted">Każda wartość ma źródło, datę stanu i pewność</caption>
        <thead>
          <tr>
            <th scope="col">Fakt</th>
            <th scope="col">Wartość</th>
            <th scope="col">Źródło</th>
            <th scope="col">Stan na</th>
            <th scope="col">Pewność</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const known = row.value !== null;
            const url = source ? safeHttpUrl(source.sourceUrl) : null;
            return (
              <tr key={row.key}>
                <th scope="row">{row.label}</th>
                <td data-label="Wartość">{known ? row.value : <span className="muted">{UNKNOWN}</span>}</td>
                <td data-label="Źródło">
                  {known && source ? (
                    <>
                      {url ? (
                        <a href={url} rel="noopener noreferrer nofollow">
                          {source.source}
                        </a>
                      ) : (
                        source.source
                      )}
                      <br />
                      <span className="muted">{source.method}</span>
                    </>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
                <td data-label="Stan na">{known ? asOfLabel(row.asOf) : <span className="muted">—</span>}</td>
                <td data-label="Pewność">
                  {known && source ? (
                    <span className="badge" data-confidence={source.confidence}>
                      {confidenceLabel(source.confidence)}
                    </span>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function GoverningBodyNote({ body }: { body: GoverningBody }) {
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
