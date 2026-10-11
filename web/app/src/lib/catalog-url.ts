/**
 * Candidate ladder for the catalog API base URL (SPEC: web reads CATALOG_API_URL at request time).
 * Order: explicit env var -> Aspire/orchestrator service discovery -> localhost fallback.
 * Read on every call; never inlined at build time (no NEXT_PUBLIC_* for addresses).
 */
export type CatalogSource = "CATALOG_API_URL" | "services__api__http__0" | "default";

export interface CatalogCandidate {
  readonly source: CatalogSource;
  readonly url: string;
}

const DEFAULT_CATALOG_URL = "http://localhost:8080";

function normalise(raw: string | undefined): string | null {
  if (raw === undefined) return null;
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  try {
    const parsed = new URL(trimmed);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return null;
    return trimmed.replace(/\/+$/, "");
  } catch {
    return null;
  }
}

export function catalogCandidates(env: NodeJS.ProcessEnv = process.env): CatalogCandidate[] {
  const candidates: CatalogCandidate[] = [];
  const explicit = normalise(env.CATALOG_API_URL);
  if (explicit) candidates.push({ source: "CATALOG_API_URL", url: explicit });
  const discovered = normalise(env["services__api__http__0"]);
  if (discovered) candidates.push({ source: "services__api__http__0", url: discovered });
  candidates.push({ source: "default", url: DEFAULT_CATALOG_URL });
  return candidates;
}
