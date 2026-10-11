import { serialiseJsonLd } from "@/components/school-jsonld";

export interface PreschoolLd {
  readonly name: string;
  readonly url: string;
  readonly street: string | null;
  readonly postalCode: string | null;
  readonly city: string | null;
}

/**
 * schema.org Preschool as JSON-LD. Carries no rating of any kind (preschools have no quality score, ADR-0014).
 * Address fields are included only when known. Every value is escaped by serialiseJsonLd.
 */
export function PreschoolJsonLd({ preschool }: { preschool: PreschoolLd }) {
  const address =
    preschool.street || preschool.postalCode || preschool.city
      ? {
          "@type": "PostalAddress",
          ...(preschool.street ? { streetAddress: preschool.street } : {}),
          ...(preschool.postalCode ? { postalCode: preschool.postalCode } : {}),
          ...(preschool.city ? { addressLocality: preschool.city } : {}),
          addressCountry: "PL",
        }
      : null;
  const data = {
    "@context": "https://schema.org",
    "@type": "Preschool",
    name: preschool.name,
    url: preschool.url,
    ...(address ? { address } : {}),
  };
  return (
    <script
      type="application/ld+json"
      // eslint-disable-next-line react/no-danger
      dangerouslySetInnerHTML={{ __html: serialiseJsonLd(data) }}
    />
  );
}
