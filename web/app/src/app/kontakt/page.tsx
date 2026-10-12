import type { Metadata } from "next";
import { ServiceUnavailable } from "@/components/service-unavailable";
import { legalContact } from "@/lib/reviews";
import { safeHttpUrl } from "@/lib/validation";
import "../styles/static.css";

// Single point of contact for authorities and users (DSA art. 11 and 12), read from the Reviews service.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Kontakt",
  description: "Punkt kontaktowy serwisu dla organów i użytkowników.",
  alternates: { canonical: "/kontakt" },
};

export default async function ContactPage() {
  const contact = await legalContact();
  return (
    <article className="prose">
      <header className="page-header">
        <h1>Kontakt</h1>
        <p className="lead">Punkt kontaktowy dla organów państwowych i użytkowników serwisu (art. 11 i 12 DSA).</p>
      </header>
      {contact.ok ? (
        contact.data.email !== null || contact.data.url !== null ? (
          <dl className="facts">
            {contact.data.email !== null ? (
              <>
                <dt>E-mail</dt>
                <dd>
                  <a href={`mailto:${contact.data.email}`}>{contact.data.email}</a>
                </dd>
              </>
            ) : null}
            {safeHttpUrl(contact.data.url) !== null ? (
              <>
                <dt>Formularz</dt>
                <dd>
                  <a href={safeHttpUrl(contact.data.url) ?? undefined} rel="noopener noreferrer nofollow">
                    {contact.data.url}
                  </a>
                </dd>
              </>
            ) : null}
          </dl>
        ) : (
          <div className="notice notice--caution" role="note">
            Kontakt w sprawach serwisu będzie dostępny wkrótce.
          </div>
        )
      ) : contact.reason === "error" && contact.status === 503 ? (
        <div className="notice notice--caution" role="note">
          Kontakt w sprawach serwisu będzie dostępny wkrótce.
        </div>
      ) : (
        <>
          <ServiceUnavailable what="dane kontaktowe" />
          <p className="actions">
            <a href="/kontakt">Odśwież stronę</a>
            <a href="/">Wróć do wyszukiwarki</a>
          </p>
        </>
      )}
      <nav aria-label="Powiązane strony">
        <p>Zobacz też:</p>
        <ul className="plain-list">
          <li>
            <a href="/zglos-tresc">Zgłoś treść</a>
          </li>
          <li>
            <a href="/regulamin">Regulamin</a>
          </li>
          <li>
            <a href="/prywatnosc">Polityka prywatności</a>
          </li>
        </ul>
      </nav>
    </article>
  );
}
