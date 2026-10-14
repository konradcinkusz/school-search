"use client";

import { MarkerClusterer, type Cluster, type Renderer } from "@googlemaps/markerclusterer";
import { useCallback, useEffect, useRef, useState } from "react";
import type { MapMarker } from "@/lib/api";
import { loadGoogleMaps, onGoogleMapsAuthFailure } from "@/lib/map-loader";
import {
  DEFAULT_ZOOM,
  POLAND_CENTER,
  boundsAround,
  clusterIconUrl,
  markerIconUrl,
  normaliseBounds,
  type Bounds,
} from "@/lib/map-markers";

/**
 * Imperative Google Maps wrapper for MapExplorer (ADR-0022). It only draws what the page passes in: neutral markers
 * (one icon per kind, never per score), neutral clusters, and reports the viewport after the map goes idle.
 * Nothing here runs without an API key; the Maps script is requested from Google only after a key was handed in.
 */
export type MapStatus = "idle" | "loading" | "ready" | "error";
export type GeocodeOutcome = "found" | "not_found" | "unavailable";

interface Options {
  /** The browser key from /api/config, or null: then the hook does nothing and Google is never contacted. */
  readonly apiKey: string | null;
  readonly markers: readonly MapMarker[];
  readonly selectedSlug: string | null;
  readonly onSelect: (marker: MapMarker) => void;
  readonly onViewport: (bounds: Bounds) => void;
}

const MAX_Z = 1_000_000;

const prefersReducedMotion = (): boolean =>
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export function useGoogleMap({ apiKey, markers, selectedSlug, onSelect, onViewport }: Options) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<google.maps.Map | null>(null);
  const clustererRef = useRef<MarkerClusterer | null>(null);
  const geocoderRef = useRef<google.maps.Geocoder | null>(null);
  const markerCtorRef = useRef<typeof google.maps.Marker | null>(null);
  const drawn = useRef(new Map<string, { marker: google.maps.Marker; kind: MapMarker["kind"]; selected: boolean }>());
  const onSelectRef = useRef(onSelect);
  const onViewportRef = useRef(onViewport);
  const [status, setStatus] = useState<MapStatus>(apiKey === null ? "idle" : "loading");
  const [failure, setFailure] = useState<string | null>(null);

  useEffect(() => {
    onSelectRef.current = onSelect;
    onViewportRef.current = onViewport;
  });

  // Create the map once, when a key arrives.
  useEffect(() => {
    if (apiKey === null) return;
    let cancelled = false;
    setStatus("loading");
    onGoogleMapsAuthFailure(() => {
      if (cancelled) return;
      setFailure("Mapa Google odrzuciła klucz. Wyniki są pokazane jako lista.");
      setStatus("error");
    });
    loadGoogleMaps(apiKey)
      .then((libs) => {
        const container = containerRef.current;
        if (cancelled || container === null) return;
        const map = new libs.maps.Map(container, {
          center: POLAND_CENTER,
          zoom: DEFAULT_ZOOM,
          minZoom: 5,
          mapTypeControl: false,
          streetViewControl: false,
          fullscreenControl: false,
          clickableIcons: false,
          keyboardShortcuts: true,
        });
        markerCtorRef.current = libs.marker.Marker;
        geocoderRef.current = libs.geocoding !== null ? new libs.geocoding.Geocoder() : null;
        const renderer: Renderer = {
          render: ({ count, position }: Cluster) =>
            new libs.marker.Marker({
              position,
              title: `Grupa placówek: ${count}`,
              icon: {
                url: clusterIconUrl(count),
                scaledSize: new google.maps.Size(44, 44),
                anchor: new google.maps.Point(22, 22),
              },
              zIndex: MAX_Z + count,
            }),
        };
        clustererRef.current = new MarkerClusterer({ map, markers: [], renderer });
        let timer: ReturnType<typeof setTimeout> | undefined;
        map.addListener("idle", () => {
          clearTimeout(timer);
          timer = setTimeout(() => {
            const b = map.getBounds();
            if (!b) return;
            const ne = b.getNorthEast();
            const sw = b.getSouthWest();
            const bounds = normaliseBounds({ south: sw.lat(), west: sw.lng(), north: ne.lat(), east: ne.lng() });
            if (bounds) onViewportRef.current(bounds);
          }, 350);
        });
        mapRef.current = map;
        setStatus("ready");
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        console.warn("[map] Google Maps could not start", error);
        setFailure("Nie udało się wczytać mapy Google. Wyniki są pokazane jako lista.");
        setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [apiKey]);

  // Sync markers: add new, remove stale, restyle the selected one. Clusters are rebuilt by the clusterer.
  useEffect(() => {
    const clusterer = clustererRef.current;
    const Marker = markerCtorRef.current;
    if (status !== "ready" || clusterer === null || Marker === null) return;
    try {
      const wanted = new Map(markers.map((m) => [m.slug, m]));
      const stale: google.maps.Marker[] = [];
      for (const [slug, entry] of drawn.current) {
        if (!wanted.has(slug)) {
          stale.push(entry.marker);
          drawn.current.delete(slug);
        }
      }
      if (stale.length > 0) clusterer.removeMarkers(stale, true);
      const fresh: google.maps.Marker[] = [];
      for (const m of markers) {
        const selected = m.slug === selectedSlug;
        const existing = drawn.current.get(m.slug);
        const size = selected ? 36 : 30;
        const icon = (): google.maps.Icon => ({
          url: markerIconUrl(m.kind, selected),
          scaledSize: new google.maps.Size(size, size),
          anchor: new google.maps.Point(size / 2, size / 2),
        });
        if (existing) {
          if (existing.selected !== selected) {
            existing.marker.setIcon(icon());
            existing.marker.setZIndex(selected ? MAX_Z : null);
            existing.selected = selected;
          }
          continue;
        }
        const marker = new Marker({
          position: { lat: m.latitude, lng: m.longitude },
          title: `${m.name}`,
          icon: icon(),
          zIndex: selected ? MAX_Z : null,
        });
        marker.addListener("click", () => onSelectRef.current(m));
        drawn.current.set(m.slug, { marker, kind: m.kind, selected });
        fresh.push(marker);
      }
      if (fresh.length > 0) clusterer.addMarkers(fresh, true);
      clusterer.render();
    } catch (error) {
      // A failure while drawing must never take the page down: fall back to the list.
      console.warn("[map] could not draw markers", error);
      setFailure("Nie udało się narysować mapy. Wyniki są pokazane jako lista.");
      setStatus("error");
    }
  }, [markers, selectedSlug, status]);

  const flyTo = useCallback((lat: number, lng: number, zoom: number) => {
    const map = mapRef.current;
    if (map === null) return;
    if (prefersReducedMotion()) {
      map.setCenter({ lat, lng });
      map.setZoom(Math.max(map.getZoom() ?? 0, zoom));
    } else {
      map.panTo({ lat, lng });
      map.setZoom(Math.max(map.getZoom() ?? 0, zoom));
    }
  }, []);

  const resize = useCallback(() => {
    const map = mapRef.current;
    if (map === null) return;
    const center = map.getCenter();
    google.maps.event.trigger(map, "resize");
    if (center) map.setCenter(center);
  }, []);

  /** Pans the map to a typed place. The result is only a viewport: it never decides a catchment (ADR-0010, ADR-0022). */
  const showPlace = useCallback(async (place: string, radiusKm: number | undefined): Promise<GeocodeOutcome> => {
    const map = mapRef.current;
    const geocoder = geocoderRef.current;
    if (map === null || geocoder === null) return "unavailable";
    try {
      const { results } = await geocoder.geocode({ address: place, region: "PL", componentRestrictions: { country: "PL" } });
      const first = results[0];
      if (!first) return "not_found";
      const loc = first.geometry.location;
      if (radiusKm !== undefined) {
        const b = boundsAround(loc.lat(), loc.lng(), radiusKm);
        map.fitBounds({ south: b.south, west: b.west, north: b.north, east: b.east });
      } else if (first.geometry.viewport) {
        map.fitBounds(first.geometry.viewport);
      } else {
        map.setCenter(loc);
        map.setZoom(14);
      }
      return "found";
    } catch (error) {
      const code = (error as { code?: string } | null)?.code;
      return code === "ZERO_RESULTS" ? "not_found" : "unavailable";
    }
  }, []);

  return { containerRef, status, failure, flyTo, resize, showPlace };
}
