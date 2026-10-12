import Link from "next/link";
import type { PreschoolFactsBlock, PreschoolListItem } from "@/lib/api";
import { freePlacesValue, hoursLabel, sourceLine, tuitionLabel } from "@/lib/preschool";

/**
 * Facts per preschool slug, from the profile. The list endpoint carries fee and vacancies only, so source, date and
 * opening hours come from the profile. A slug with no entry (or a null entry) shows that its source is unknown.
 */
export type PreschoolFactsBySlug = Readonly<Record<string, PreschoolFactsBlock | null | undefined>>;

function Fact({ label, value, meta }: { readonly label: string; readonly value: string | null; readonly meta: string | null }) {
  return (
    <div className="school-list__fact">
      <dt>{label}</dt>
      <dd>
        <span className="school-list__value">{value ?? <span className="muted">brak danych</span>}</span>
        {value !== null && meta !== null ? <span className="school-list__source muted">{meta}</span> : null}
      </dd>
    </div>
  );
}

/** A list of preschools: practical facts only, no score, rank or grade. Names render as text inside a link. */
export function PreschoolList({
  items,
  emptyText,
  factsBySlug,
}: {
  readonly items: readonly PreschoolListItem[];
  readonly emptyText: string;
  readonly factsBySlug?: PreschoolFactsBySlug;
}) {
  if (items.length === 0) {
    return <p className="muted">{emptyText}</p>;
  }
  return (
    <ul className="school-list">
      {items.map((preschool) => {
        const facts = factsBySlug?.[preschool.slug] ?? null;
        const tuition = tuitionLabel(preschool.monthlyTuition);
        const places = freePlacesValue(preschool.freePlacesCount);
        const hours = facts ? hoursLabel(facts.hours) : null;
        const noSource = facts ? null : "Źródło i data: brak danych";
        return (
          <li key={preschool.slug} className="school-list__item school-list__item--facts card">
            <div>
              <h3 className="school-list__name">
                <Link href={`/przedszkola/${preschool.slug}`}>{preschool.name}</Link>
              </h3>
              <p className="muted school-list__meta">
                {preschool.gminaName ??
                  (preschool.gminaTeryt ? `Gmina TERYT ${preschool.gminaTeryt}` : "Gmina: brak danych")}
                {preschool.isPublic ? " · przedszkole publiczne" : " · przedszkole niepubliczne"}
              </p>
            </div>
            <dl className="school-list__facts">
              <Fact
                label="Czesne"
                value={tuition}
                meta={noSource ?? sourceLine(facts?.source, facts?.source.asOf)}
              />
              <Fact
                label="Wolne miejsca"
                value={places}
                meta={noSource ?? sourceLine(facts?.source, facts?.freePlaces.asOf)}
              />
              <Fact label="Godziny otwarcia" value={hours} meta={noSource ?? sourceLine(facts?.source)} />
            </dl>
          </li>
        );
      })}
    </ul>
  );
}
