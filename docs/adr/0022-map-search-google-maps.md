# ADR-0022: Map search with Google Maps JavaScript API and a runtime key

- **Status:** Proposed (the owner decides; the provider choice and its billing are the owner's. Nothing here is in force until the owner marks it Accepted.)
- **Date:** 2026-10-14
- **Deciders:** owner (Konrad Cinkusz), not yet decided. Drafted by an agent working to the Phase M contract.

## Context

Parents think in places, not in lists. A map page (`/mapa`) lets a visitor browse schools and preschools around a place.
Constraints that shape the choice:

- One image serves every environment (frontend-BFF guide, runtime configuration): addresses and keys are read from the
  environment per request, never inlined at build time (`NEXT_PUBLIC_*` is not used).
- The browser reaches the catalog only through the BFF. The page's Content-Security-Policy is strict
  (`connect-src 'self'`, no third-party scripts). A map provider needs script, image, style, font and connect hosts.
- P8: an optional dependency must degrade. CI and any environment without a key must still work.
- ADR-0010: the school catchment (obwod) is decided by the gmina's street list, never by coordinates or distance. A map
  must not become a second, guessed catchment.
- AGENTS.md: no score, rank or "good/bad" colour anywhere. Markers must be neutral.
- Not known: the owner's Google Cloud billing account, quotas and budget, and whether Google's terms and attribution
  requirements suit the portal's licence position. These are the owner's to confirm; nothing here is a legal conclusion.

## Decision

1. Use the **Google Maps JavaScript API**, loaded in the browser with a **key read at runtime** from the server variable
   `GOOGLE_MAPS_API_KEY` and handed to the page by `/api/config` (`googleMapsApiKey`, present only when set and
   well-formed). The key is a browser key: public by design, protected by restrictions set on the key, not by secrecy.
2. **Content-Security-Policy is built per request** in `src/middleware.ts` (`lib/map-csp.ts`), because
   `next.config.ts` `headers()` is evaluated at build time. Without a key the policy is exactly the previous strict
   baseline. With a key only these are added: `script-src https://maps.googleapis.com`; `style-src
   https://fonts.googleapis.com`; `img-src blob: https://maps.gstatic.com https://*.gstatic.com https://*.googleapis.com`;
   `connect-src https://maps.googleapis.com https://*.googleapis.com`; `font-src https://fonts.gstatic.com`;
   `frame-src 'none'`; `worker-src 'self' blob:`. Catalog data still goes same-origin, so `connect-src 'self'` carries it.
3. **Fallback list.** No key (or a script that cannot load, or a key Google rejects) renders the same filters and the
   same results as a list, with the notice "Mapa jest niedostępna: nie skonfigurowano klucza Google Maps. Wyniki są
   pokazane jako lista." The list is also the keyboard and screen-reader alternative when the map works. CI exercises
   this path.
4. **Data through the BFF.** `/api/map/markers` validates and clamps the browser's query (area, kind, name of at least
   two characters, gmina, public only, tuition ceiling, free places, limit up to 500), forwards only the contract
   parameters to the catalog's `/api/v1/map/markers`, re-emits only the contract fields, and maps upstream errors to
   Polish messages. The browser never sees a backend address.
5. **Neutral markers.** One colour and shape for all schools, another for all preschools; clusters are neutral ink.
   Never coloured, sized or ordered by a score, rank, review or sponsorship. The truncated subset is arbitrary (ordered
   by slug upstream), and the page says so: "Przybliż mapę, aby zobaczyć wszystkie placówki."
6. **No geocoding for the catchment.** The optional place box uses the Google `Geocoder` in the browser, only when a key
   exists, only to pan or zoom the map. Its result is never sent to the catalog and never decides a catchment. The UI
   says: "Mapa służy do przeglądania okolicy. Obwód szkolny sprawdzisz po adresie na stronie głównej."
7. **Dependencies.** `@googlemaps/markerclusterer` 2.6.2 (clustering) and `@types/google.maps` 3.66.3 (types only).
   The loader is a small in-repo module (`lib/map-loader.ts`) using Google's dynamic library import, so
   `@googlemaps/js-api-loader` is not added.

### Restrictions to set on the key (owner action, in Google Cloud)

- Application restriction: **HTTP referrers**, listing only the portal's production and preview origins.
- API restriction: enable **Maps JavaScript API**, and **Geocoding API** for the place box. Nothing else.
- Billing, budget alerts and quotas are the owner's. Set a daily quota cap so a scripted abuse cannot run up a bill.
- Rotate the key if it leaks; it is configured as a platform secret (Fly secret `GOOGLE_MAPS_API_KEY`), never in a file.

## Options considered

| Option | For | Against |
|---|---|---|
| Google Maps JavaScript API with a runtime key (chosen) | Best Polish coverage and address search; clustering library exists; the owner already works in Google Cloud | Paid beyond a free tier; third-party hosts in the CSP (only when a key is set); visitor IPs reach Google when the map loads; key must be restricted |
| MapLibre GL with a tile provider | No Google dependency; open style | Needs a tile source and its terms; geocoding is a separate service; more work for the first version |
| Leaflet with OpenStreetMap tiles | Small and simple | The public tile servers are not meant for production traffic; usage policy limits; still needs a geocoder |
| No map, lists only | Nothing to configure | Does not meet the need to browse an area |

## Consequences

- Easier: browsing by area; the same filter model (`lib/search-filters.ts`) drives the list pages, the AI filter bar and
  the map; one image still serves every environment.
- Harder: the CSP now varies with configuration and is assembled in middleware (every response passes through it); a
  change to the Maps hosts must be made in `lib/map-csp.ts`. Google may change the hosts Maps loads from; a console CSP
  violation on the map page is the signal to revisit this list.
- Privacy: when the map loads, Google receives the visitor's IP address and requests for tiles; the place box sends the
  typed place to Google. The privacy page should say so before the key is switched on in production (owner action).
  `place` is also part of the shareable URL by the filter contract.
- The map uses the legacy `google.maps.Marker`, which needs no Map ID. Google has marked it deprecated in favour of
  `AdvancedMarkerElement`, which needs a Map ID and cloud styling. Revisit when the owner sets up a Map ID.
- Not covered by CI: the Google map itself (no key, no network to Google). It is checked by hand with a restricted key.
- Exit condition: if billing or terms are unacceptable, remove `GOOGLE_MAPS_API_KEY`; the page keeps working as a list.
