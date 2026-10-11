export interface SchoolLd {
  readonly name: string;
  readonly url: string;
  readonly identifier?: string;
  readonly address?: {
    readonly streetAddress?: string;
    readonly postalCode?: string;
    readonly addressLocality?: string;
  };
}

/**
 * schema.org School as JSON-LD. Renders nothing without real school data:
 * a placeholder name would be invented data.
 */
export function SchoolJsonLd({ school }: { school: SchoolLd | null }) {
  if (!school) return null;
  const data = {
    "@context": "https://schema.org",
    "@type": "School",
    name: school.name,
    url: school.url,
    ...(school.identifier ? { identifier: school.identifier } : {}),
    ...(school.address
      ? {
          address: {
            "@type": "PostalAddress",
            ...school.address,
            addressCountry: "PL",
          },
        }
      : {}),
  };
  // Escape "<" so no JSON string can close the script element.
  const json = JSON.stringify(data).replace(/</g, "\\u003c");
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: json }} />;
}
