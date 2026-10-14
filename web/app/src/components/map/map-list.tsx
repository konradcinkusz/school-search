"use client";

import Link from "next/link";
import { useState } from "react";
import type { MapMarker } from "@/lib/api";
import { formatInteger } from "@/lib/format";
import { KIND_LABEL, preschoolFacts, profileHref, publicLabel, sortByName } from "@/lib/map-markers";

const PAGE = 50;

/**
 * The result list: the keyboard and screen-reader alternative to the map, and the whole page when no map is available.
 * Alphabetical by name (never by quality). Names are text inside a link to the profile.
 */
export function MapList({
  items,
  canShowOnMap,
  onShowOnMap,
  headingId,
}: {
  readonly items: readonly MapMarker[];
  readonly canShowOnMap: boolean;
  readonly onShowOnMap: (marker: MapMarker) => void;
  readonly headingId: string;
}) {
  const [visible, setVisible] = useState(PAGE);
  const sorted = sortByName(items);
  const shown = sorted.slice(0, visible);
  return (
    <>
      {sorted.length === 0 ? (
        <p className="muted">Brak placówek do pokazania. Zmień filtry albo przesuń mapę.</p>
      ) : (
        <ul className="map-list" aria-labelledby={headingId}>
          {shown.map((marker) => {
            const facts = marker.kind === "preschool" ? preschoolFacts(marker) : null;
            return (
              <li key={`${marker.kind}-${marker.slug}`} className="map-list__item card">
                <div className="map-list__head">
                  <span className="map-kind" data-kind={marker.kind}>
                    <span className="map-kind__swatch" aria-hidden="true" />
                    {KIND_LABEL[marker.kind]}
                  </span>
                </div>
                <h3 className="map-list__name">
                  <Link href={profileHref(marker)}>{marker.name}</Link>
                </h3>
                <p className="muted map-list__meta">
                  {marker.gminaName ?? "Gmina: brak danych"} · {publicLabel(marker)}
                </p>
                {facts ? (
                  <dl className="map-list__facts">
                    <div>
                      <dt>Czesne</dt>
                      <dd>{facts.tuition === "brak danych" ? <span className="muted">brak danych</span> : facts.tuition}</dd>
                    </div>
                    <div>
                      <dt>Wolne miejsca</dt>
                      <dd>{facts.freePlaces === "brak danych" ? <span className="muted">brak danych</span> : facts.freePlaces}</dd>
                    </div>
                  </dl>
                ) : null}
                {canShowOnMap ? (
                  <p className="actions">
                    <button
                      type="button"
                      className="button button--secondary"
                      onClick={() => onShowOnMap(marker)}
                      aria-label={`Pokaż na mapie: ${marker.name}`}
                    >
                      Pokaż na mapie
                    </button>
                  </p>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
      {sorted.length > shown.length ? (
        <p className="actions">
          <button type="button" className="button button--secondary" onClick={() => setVisible((n) => n + PAGE)}>
            Pokaż więcej ({formatInteger(sorted.length - shown.length)})
          </button>
        </p>
      ) : null}
    </>
  );
}
