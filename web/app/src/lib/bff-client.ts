// Browser-side helper for calls to the BFF (/api/auth/* and /api/proxy/*). Browser-safe: no server imports here.
// Every answer is parsed defensively. A network failure yields null, so callers can show the unavailable state.

export interface BffResponse {
  readonly status: number;
  readonly body: unknown;
}

const CLIENT_TIMEOUT_MS = 20_000;

export async function bffRequest(
  url: string,
  options: { readonly method?: "GET" | "POST" | "DELETE"; readonly body?: unknown } = {},
): Promise<BffResponse | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), CLIENT_TIMEOUT_MS);
  try {
    const init: RequestInit = {
      method: options.method ?? "GET",
      headers: options.body === undefined ? { accept: "application/json" } : { accept: "application/json", "content-type": "application/json" },
      cache: "no-store",
      credentials: "same-origin",
      signal: controller.signal,
    };
    if (options.body !== undefined) init.body = JSON.stringify(options.body);
    const response = await fetch(url, init);
    const text = await response.text();
    let body: unknown = null;
    if (text !== "") {
      try {
        body = JSON.parse(text);
      } catch {
        body = null;
      }
    }
    return { status: response.status, body };
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** Uniform error code from {"error":{"code"}}, or null. */
export function errorCodeOf(body: unknown): string | null {
  if (isRecord(body) && isRecord(body.error) && typeof body.error.code === "string") return body.error.code;
  return null;
}

/** Short server message from {"error":{"message"}}, capped for display. Render as text only. */
export function errorMessageOf(body: unknown): string | null {
  if (isRecord(body) && isRecord(body.error) && typeof body.error.message === "string") {
    return body.error.message.slice(0, 300);
  }
  return null;
}

/** Field validation messages returned by the BFF or the Reviews service, capped and rendered as text. */
export function errorListOf(body: unknown): string[] {
  if (!isRecord(body) || !isRecord(body.error) || !Array.isArray(body.error.messages)) return [];
  return body.error.messages.filter((item): item is string => typeof item === "string").slice(0, 5);
}

export function isUnavailable(response: BffResponse | null): boolean {
  return response === null || response.status === 503 || response.status === 504 || response.status === 502;
}
