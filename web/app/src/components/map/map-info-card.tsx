"use client";

import Link from "next/link";
import { forwardRef } from "react";
import type { MapMarker } from "@/lib/api";
import { KIND_LABEL, preschoolFacts, profileHref, publicLabel } from "@/lib/map-markers";

/**
 * Card for the selected marker. Neutral: name, kind, public or private, gmina, and for a preschool the tuition and free
 * places (a missing value reads "brak danych"). No score, rank or review figure is ever shown here.
 */
export const MapInfoCard = forwardRef<
  HTMLElement,
  { readonly marker: MapMarker; readonly onClose: () => void }
>(function MapInfoCard({ marker, onClose }, ref) {
  const facts = marker.kind === "preschool" ? preschoolFacts(marker) : null;
  return (
    <aside ref={ref} className="map-card card" aria-label={`Szczegóły: ${marker.name}`} tabIndex={-1}>
      <div className="map-card__head">
        <span className="map-kind" data-kind={marker.kind}>
          <span className="map-kind__swatch" aria-hidden="true" />
          {KIND_LABEL[marker.kind]}
        </span>
        <button type="button" className="map-card__close" onClick={onClose} aria-label="Zamknij szczegóły">
          <span aria-hidden="true">×</span>
        </button>
      </div>
      <h3 className="map-card__name">{marker.name}</h3>
      <p className="muted map-card__meta">
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
      <p className="actions">
        <Link href={profileHref(marker)} className="button">
          Zobacz profil
        </Link>
      </p>
    </aside>
  );
});
