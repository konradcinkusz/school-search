import Link from "next/link";
import type { PreschoolListItem } from "@/lib/api";
import { freePlacesLabel, tuitionLabel } from "@/lib/preschool";

/** Sentence case for a label that starts a line ("wolne miejsca: 3" becomes "Wolne miejsca: 3"). */
function sentenceCase(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** A list of preschools: practical facts only, no score, rank or grade. Names render as text inside a link. */
export function PreschoolList({ items, emptyText }: { items: readonly PreschoolListItem[]; emptyText: string }) {
  if (items.length === 0) {
    return <p className="muted">{emptyText}</p>;
  }
  return (
    <ul className="school-list">
      {items.map((preschool) => {
        const tuition = tuitionLabel(preschool.monthlyTuition);
        const places = freePlacesLabel(preschool.freePlacesCount);
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
            <div className="school-list__facts">
              <p>
                Czesne: {tuition ?? <span className="muted">brak danych</span>}
              </p>
              <p>
                {places !== null ? sentenceCase(places) : <span className="muted">Wolne miejsca: brak danych</span>}
                {places !== null ? <span className="muted"> · stan z profilu</span> : null}
              </p>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
