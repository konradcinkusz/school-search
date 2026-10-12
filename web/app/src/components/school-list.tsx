import Link from "next/link";
import type { SchoolListItem } from "@/lib/api";
import { formatDistanceKm } from "@/lib/format";
import { ScoreBadge } from "@/components/score-badge";

/** A list of schools. Every name is rendered as text inside a link; the score block sits beside it, complete. */
export function SchoolList({ items, emptyText }: { items: readonly SchoolListItem[]; emptyText: string }) {
  if (items.length === 0) {
    return <p className="empty-note">{emptyText}</p>;
  }
  return (
    <ul className="school-list">
      {items.map((school) => (
        <li key={school.slug} className="school-list__item school-list__item--scored card">
          <div className="school-list__main">
            <h3 className="school-list__name">
              <Link href={`/szkoly/${school.slug}`}>{school.name}</Link>
            </h3>
            <p className="muted school-list__meta">
              <span className="school-list__place">{school.gminaName ?? "Nieznana gmina"}</span>
              {school.distanceKm !== null ? ` · ok. ${formatDistanceKm(school.distanceKm)}` : null}
              {school.isPublic ? null : " · szkoła niepubliczna"}
              {school.isSpecial ? " · szkoła specjalna" : null}
            </p>
          </div>
          <ScoreBadge status={school.scoreStatus} block={school.score} variant="compact" />
        </li>
      ))}
    </ul>
  );
}
