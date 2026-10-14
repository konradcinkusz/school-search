"use client";

import Link from "next/link";
import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { AiFilterBar } from "@/components/filters/ai-filter-bar";
import { MapInfoCard } from "@/components/map/map-info-card";
import { MapList } from "@/components/map/map-list";
import { useGoogleMap } from "@/components/map/use-google-map";
import type { MapMarker, MapMarkersResponse } from "@/lib/api";
import { POLAND_BOUNDS, countMessage, markerQuery, type Bounds } from "@/lib/map-markers";
import { describeFilters, filtersToSearchParams, type SearchFilters } from "@/lib/search-filters";

/**
 * Map search (ADR-0022). Filters live in the URL; markers are fetched for the visible area through /api/map/markers.
 * With a Google Maps key (handed over by /api/config at runtime) the page shows the map; without one, or when Google
 * cannot be reached, it shows the same filters and the same results as a list (P8). The list is also the keyboard
 * and screen-reader alternative to the map. Markers are neutral: never coloured, sized or ordered by a score.
 */
type ConfigState = "loading" | "ready";
type Fetched =
  | { readonly state: "idle" }
  | { readonly state: "loading"; readonly previous: MapMarkersResponse | null }
  | { readonly state: "ok"; readonly data: MapMarkersResponse }
  | { readonly state: "error"; readonly message: string; readonly previous: MapMarkersResponse | null };

const COMPACT_QUERY = "(max-width: 61.99rem)";

function useCompact(): boolean {
  const [compact, setCompact] = useState(false);
  useEffect(() => {
    const query = window.matchMedia(COMPACT_QUERY);
    const update = () => setCompact(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  return compact;
}

export function MapExplorer({ initialFilters }: { readonly initialFilters: SearchFilters }) {
  const ids = { filters: useId(), list: useId(), place: useId(), sheet: useId() };
  const compact = useCompact();

  const [filters, setFilters] = useState<SearchFilters>(initialFilters);
  const [configState, setConfigState] = useState<ConfigState>("loading");
  const [apiKey, setApiKey] = useState<string | null>(null);
  const [bounds, setBounds] = useState<Bounds | null>(null);
  const [fetched, setFetched] = useState<Fetched>({ state: "idle" });
  const [selected, setSelected] = useState<MapMarker | null>(null);
  const [view, setView] = useState<"map" | "list">("map");
  const [sheetOpen, setSheetOpen] = useState(false);
  const [placeNote, setPlaceNote] = useState<string | null>(null);

  const sheetRef = useRef<HTMLDivElement>(null);
  const filterButtonRef = useRef<HTMLButtonElement>(null);
  const cardRef = useRef<HTMLElement>(null);
  const listAnchorRef = useRef<HTMLDivElement>(null);
  const lastPlaceRef = useRef<string | null>(null);

  // Runtime configuration: the Google Maps key exists only when the server was started with GOOGLE_MAPS_API_KEY.
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/config", { signal: controller.signal, headers: { accept: "application/json" } })
      .then((response) => (response.ok ? response.json() : null))
      .then((body: unknown) => {
        const key = (body as { googleMapsApiKey?: unknown } | null)?.googleMapsApiKey;
        setApiKey(typeof key === "string" && key !== "" ? key : null);
        setConfigState("ready");
      })
      .catch((error: unknown) => {
        if ((error as { name?: string } | null)?.name === "AbortError") return;
        setApiKey(null);
        setConfigState("ready");
      });
    return () => controller.abort();
  }, []);

  const filtersKey = filtersToSearchParams(filters).toString();

  // Keep the URL shareable. replaceState: filter edits do not pile up history entries.
  useEffect(() => {
    const url = filtersKey === "" ? window.location.pathname : `${window.location.pathname}?${filtersKey}`;
    window.history.replaceState(null, "", url);
  }, [filtersKey]);

  const onSelect = useCallback((marker: MapMarker) => {
    setSelected(marker);
  }, []);

  const [markers, setMarkers] = useState<readonly MapMarker[]>([]);
  const map = useGoogleMap({
    apiKey,
    markers,
    selectedSlug: selected?.slug ?? null,
    onSelect,
    onViewport: setBounds,
  });
  const mapReady = map.status === "ready";
  const mapPending = configState === "loading" || (apiKey !== null && map.status === "loading");
  const mapOff = configState === "ready" && (apiKey === null || map.status === "error");

  // List fallback: the whole of Poland. With a map: the viewport reported by the map's idle event.
  const effectiveBounds: Bounds | null = mapOff ? POLAND_BOUNDS : mapReady ? bounds : null;

  useEffect(() => {
    if (effectiveBounds === null) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setFetched((prev) => ({
        state: "loading",
        previous: prev.state === "ok" ? prev.data : prev.state === "loading" || prev.state === "error" ? prev.previous : null,
      }));
      fetch(`/api/map/markers?${markerQuery(effectiveBounds, filters).toString()}`, {
        signal: controller.signal,
        headers: { accept: "application/json" },
      })
        .then(async (response) => {
          const body: unknown = await response.json().catch(() => null);
          if (!response.ok) {
            const message = (body as { error?: { message?: unknown } } | null)?.error?.message;
            throw new Error(typeof message === "string" ? message : "Nie udało się pobrać placówek.");
          }
          return body as MapMarkersResponse;
        })
        .then((data) => {
          setFetched({ state: "ok", data });
          setMarkers(data.items);
        })
        .catch((error: unknown) => {
          if ((error as { name?: string } | null)?.name === "AbortError") return;
          const message = error instanceof Error && error.message !== "" ? error.message : "Nie udało się pobrać placówek.";
          setFetched((prev) => ({
            state: "error",
            message,
            previous: prev.state === "ok" ? prev.data : prev.state === "loading" || prev.state === "error" ? prev.previous : null,
          }));
        });
    }, 150);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
    // filtersKey covers every filter that is sent; effectiveBounds is a new object only when the viewport changed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effectiveBounds, filtersKey]);

  // A typed place pans the map (Geocoder, client side). It never decides a catchment.
  useEffect(() => {
    const place = filters.place;
    if (!mapReady || place === undefined || place.length < 3) {
      if (place === undefined) {
        lastPlaceRef.current = null;
        setPlaceNote(null);
      }
      return;
    }
    const signature = `${place}|${filters.radiusKm ?? ""}`;
    if (lastPlaceRef.current === signature) return;
    const timer = setTimeout(() => {
      lastPlaceRef.current = signature;
      void map.showPlace(place, filters.radiusKm).then((outcome) => {
        setPlaceNote(
          outcome === "found"
            ? `Mapa pokazuje okolicę: ${place}.`
            : outcome === "not_found"
              ? `Nie znaleziono miejsca: ${place}. Mapa nie została przesunięta.`
              : "Wyszukiwanie miejsca jest teraz niedostępne. Przesuń mapę ręcznie.",
        );
      });
    }, 900);
    return () => clearTimeout(timer);
    // map.showPlace is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mapReady, filters.place, filters.radiusKm]);

  // The map container is hidden while the list is shown on a phone; tell Google its size changed when it comes back.
  useEffect(() => {
    if (mapReady && view === "map") map.resize();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view, mapReady, compact]);

  // Selecting a marker moves focus to its card so keyboard and screen-reader users land on the details.
  useEffect(() => {
    if (selected !== null) cardRef.current?.focus();
  }, [selected]);

  // Filter sheet (phone): Escape closes, Tab stays inside, focus returns to the button that opened it.
  useEffect(() => {
    if (!sheetOpen || !compact) return;
    sheetRef.current?.focus();
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setSheetOpen(false);
        filterButtonRef.current?.focus();
        return;
      }
      if (event.key !== "Tab" || sheetRef.current === null) return;
      const focusable = Array.from(
        sheetRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]',
        ),
      ).filter((el) => el.getClientRects().length > 0);
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!first || !last) return;
      if (event.shiftKey && (document.activeElement === first || document.activeElement === sheetRef.current)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [sheetOpen, compact]);

  function closeSheet() {
    setSheetOpen(false);
    filterButtonRef.current?.focus();
  }

  function showOnMap(marker: MapMarker) {
    setView("map");
    setSelected(marker);
    map.flyTo(marker.latitude, marker.longitude, 15);
  }

  const response: MapMarkersResponse | null =
    fetched.state === "ok" ? fetched.data : fetched.state === "loading" || fetched.state === "error" ? fetched.previous : null;
  const items = response?.items ?? [];
  const filterCount = useMemo(() => describeFilters(filters).length, [filters]);
  const sheetIsDialog = compact && sheetOpen;

  let countText: string;
  if (fetched.state === "error") countText = fetched.message;
  else if (response !== null) countText = countMessage(response) + (fetched.state === "loading" ? " Odświeżanie…" : "");
  else countText = configState === "loading" || fetched.state === "loading" ? "Wczytywanie placówek…" : "";

  const showMapUi = configState === "ready" && !mapOff;
  const listHidden = compact && showMapUi && view === "map";
  const mapHidden = compact && view === "list";

  return (
    <div className="map-explorer" data-map={mapOff ? "off" : mapReady ? "ready" : "pending"} data-view={view}>
      <noscript>
        <p className="notice" role="note">
          Mapa wymaga włączonego JavaScriptu. Placówki znajdziesz też na liście przedszkoli i w wyszukiwarce na stronie
          głównej.
        </p>
      </noscript>

      {mapOff ? (
        <p className="notice notice--caution map-explorer__notice" role="note">
          {map.failure ?? "Mapa jest niedostępna: nie skonfigurowano klucza Google Maps. Wyniki są pokazane jako lista."}
        </p>
      ) : null}

      <p className="map-help muted">
        Mapa służy do przeglądania okolicy. Obwód szkolny sprawdzisz po adresie na{" "}
        <Link href="/">stronie głównej</Link>.
      </p>

      <div className="map-toolbar">
        <button
          ref={filterButtonRef}
          type="button"
          className="button button--secondary map-toolbar__filters"
          aria-expanded={sheetOpen}
          aria-controls={ids.sheet}
          onClick={() => setSheetOpen((open) => !open)}
        >
          {filterCount > 0 ? `Filtry (${filterCount})` : "Filtry"}
        </button>
        {showMapUi ? (
          <div role="group" aria-label="Widok wyników" className="map-toolbar__views">
            <button type="button" className="button button--secondary" aria-pressed={view === "map"} onClick={() => setView("map")}>
              Mapa
            </button>
            <button type="button" className="button button--secondary" aria-pressed={view === "list"} onClick={() => setView("list")}>
              Lista
            </button>
          </div>
        ) : null}
      </div>

      <div className="map-layout">
        <div className="map-side">
      {sheetIsDialog ? <div className="map-sheet-backdrop" onClick={closeSheet} aria-hidden="true" /> : null}
      <div
        id={ids.sheet}
        ref={sheetRef}
        className="map-filters"
        data-open={sheetOpen ? "true" : "false"}
        role={sheetIsDialog ? "dialog" : undefined}
        aria-modal={sheetIsDialog ? true : undefined}
        aria-label={sheetIsDialog ? "Filtry mapy" : undefined}
        tabIndex={sheetIsDialog ? -1 : undefined}
      >
        <AiFilterBar filters={filters} onChange={setFilters} context="map">
          {compact ? (
            <p className="actions map-filters__done">
              <button type="button" className="button" onClick={closeSheet}>
                Pokaż wyniki
              </button>
            </p>
          ) : null}
        </AiFilterBar>
      </div>

      <div className="map-results">
        <p className="map-count" role="status" aria-live="polite">
          {countText}
        </p>
        {placeNote !== null ? (
          <p className="map-place-note muted" role="status">
            {placeNote}
          </p>
        ) : null}
        {filters.place !== undefined && mapOff ? (
          <p className="map-place-note muted">Miejsce „{filters.place}” zostanie użyte, gdy mapa będzie dostępna.</p>
        ) : null}
        {response?.truncated ? (
          <p className="notice map-truncated" role="note">
            {mapOff
              ? "Pokazano tylko część placówek. Zawęź filtry, aby zobaczyć wszystkie."
              : "Przybliż mapę, aby zobaczyć wszystkie placówki."}
          </p>
        ) : null}
        {filters.q !== undefined && filters.q.length < 2 ? (
          <p className="muted">Nazwa musi mieć co najmniej dwa znaki, aby zawęzić wyniki.</p>
        ) : null}

        <div id={ids.list} ref={listAnchorRef} tabIndex={-1} className="map-list-wrap" hidden={listHidden}>
          <h2 id={`${ids.list}-h`} className="map-list-heading">
            Lista placówek
          </h2>
          <MapList items={items} canShowOnMap={mapReady} onShowOnMap={showOnMap} headingId={`${ids.list}-h`} />
        </div>
      </div>
        </div>
        <div className="map-main">
        {showMapUi ? (
          <section className="map-stage" aria-label="Mapa placówek" hidden={mapHidden}>
            <div className="map-canvas-wrap">
              <div ref={map.containerRef} className="map-canvas" data-ready={mapReady ? "true" : "false"} />
              {mapPending ? <p className="map-canvas__loading muted">Wczytywanie mapy…</p> : null}
              <ul className="map-legend" aria-label="Legenda znaczników">
                <li>
                  <span className="map-kind" data-kind="school">
                    <span className="map-kind__swatch" aria-hidden="true" />
                    Szkoła
                  </span>
                </li>
                <li>
                  <span className="map-kind" data-kind="preschool">
                    <span className="map-kind__swatch" aria-hidden="true" />
                    Przedszkole
                  </span>
                </li>
              </ul>
              {selected !== null ? (
                <MapInfoCard
                  key={selected.slug}
                  ref={cardRef}
                  marker={selected}
                  onClose={() => {
                    setSelected(null);
                    listAnchorRef.current?.focus();
                  }}
                />
              ) : null}
            </div>
          </section>
        ) : null}

        </div>
      </div>
    </div>
  );
}
