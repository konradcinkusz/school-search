"use client";

import Link from "next/link";
import { useState } from "react";
import { AiFilterBar } from "@/components/filters/ai-filter-bar";
import { listTarget } from "@/components/filters/helpers";
import { EMPTY_FILTERS, hasActiveFilters, type SearchFilters } from "@/lib/search-filters";

/**
 * Home-page "Opisz czego szukasz": a thin wrapper over the shared filter bar (context "list"). Nothing navigates by
 * itself; the visitor follows the link. Place and distance are reported honestly and handed to the map.
 */
export function SearchIntentBox() {
  const [filters, setFilters] = useState<SearchFilters>(EMPTY_FILTERS);
  const target = hasActiveFilters(filters) ? listTarget(filters) : null;

  return (
    <AiFilterBar filters={filters} onChange={setFilters} context="list">
      {target !== null ? (
        <div className="filter-bar__apply">
          <p className="actions">
            <Link href={target.href} className="button">
              Pokaż wyniki z tymi filtrami
            </Link>
          </p>
          {target.notes.length > 0 ? <p className="muted">{target.notes.join(" ")}</p> : null}
          {target.unapplied.length > 0 ? (
            <p className="muted">Wyników nie zawężamy według: {target.unapplied.join("; ")}.</p>
          ) : null}
          {target.mapHref !== null ? (
            <p>
              <Link href={target.mapHref}>Zobacz te filtry na mapie</Link>
            </p>
          ) : null}
        </div>
      ) : null}
    </AiFilterBar>
  );
}
