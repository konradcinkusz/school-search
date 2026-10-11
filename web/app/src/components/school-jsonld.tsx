export interface SchoolLd {
  readonly name: string;
  readonly url: string;
}

/** Escapes characters that could end the script element or change how the JSON is parsed. */
export function serialiseJsonLd(data: unknown): string {
  return JSON.stringify(data)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/&/g, "\\u0026")
    .replace(/\u2028/g, "\\u2028")
    .replace(/\u2029/g, "\\u2029");
}

/**
 * schema.org School as JSON-LD, filled from the school profile. The only place that uses raw HTML;
 * every value is escaped by serialiseJsonLd. Renders nothing without a real school.
 */
export function SchoolJsonLd({ school }: { school: SchoolLd | null }) {
  if (!school) return null;
  const data = {
    "@context": "https://schema.org",
    "@type": "School",
    name: school.name,
    url: school.url,
  };
  return (
    <script
      type="application/ld+json"
      // eslint-disable-next-line react/no-danger
      dangerouslySetInnerHTML={{ __html: serialiseJsonLd(data) }}
    />
  );
}
