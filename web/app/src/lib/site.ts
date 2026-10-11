/** Public site origin, read at request time (sitemap/robots). Not a NEXT_PUBLIC_ build-time value. */
export function siteOrigin(env: NodeJS.ProcessEnv = process.env): string {
  const raw = env.SITE_URL?.trim();
  if (raw) {
    try {
      const parsed = new URL(raw);
      if (parsed.protocol === "http:" || parsed.protocol === "https:") return parsed.origin;
    } catch {
      // fall through to the local default
    }
  }
  return "http://localhost:3000";
}
