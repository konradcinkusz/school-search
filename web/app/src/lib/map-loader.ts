/**
 * Loads the Google Maps JavaScript API once per page, and only when the caller passes a key (ADR-0022).
 * No extra dependency: a single script tag with the dynamic-import bootstrap (loading=async), then importLibrary().
 * The key comes from /api/config at runtime. Without a key nothing here is called and nothing is requested from Google.
 */
type MapsLibraries = {
  readonly maps: google.maps.MapsLibrary;
  readonly marker: google.maps.MarkerLibrary;
  readonly geocoding: google.maps.GeocodingLibrary | null;
};

let pending: Promise<MapsLibraries> | null = null;

function injectScript(key: string): Promise<void> {
  return new Promise((resolve, reject) => {
    if (typeof google !== "undefined" && typeof google.maps?.importLibrary === "function") {
      resolve();
      return;
    }
    const script = document.createElement("script");
    const params = new URLSearchParams({ key, v: "weekly", loading: "async", language: "pl", region: "PL" });
    script.src = `https://maps.googleapis.com/maps/api/js?${params.toString()}`;
    script.async = true;
    script.defer = true;
    script.dataset.googleMapsLoader = "true";
    script.onload = () => resolve();
    script.onerror = () => {
      script.remove();
      reject(new Error("google-maps-script-failed"));
    };
    document.head.appendChild(script);
  });
}

/** Resolves with the Maps and Marker libraries (and the geocoding library when it can be had). Rejects when the script is blocked. */
export function loadGoogleMaps(key: string): Promise<MapsLibraries> {
  if (pending === null) {
    pending = injectScript(key)
      .then(async () => {
        const maps = (await google.maps.importLibrary("maps")) as google.maps.MapsLibrary;
        const marker = (await google.maps.importLibrary("marker")) as google.maps.MarkerLibrary;
        let geocoding: google.maps.GeocodingLibrary | null = null;
        try {
          geocoding = (await google.maps.importLibrary("geocoding")) as google.maps.GeocodingLibrary;
        } catch {
          geocoding = null;
        }
        return { maps, marker, geocoding };
      })
      .catch((error: unknown) => {
        pending = null;
        throw error;
      });
  }
  return pending;
}

/** Google reports a rejected key (referrer, billing, API not enabled) by calling this global. */
export function onGoogleMapsAuthFailure(handler: () => void): void {
  (window as unknown as { gm_authFailure?: () => void }).gm_authFailure = handler;
}
