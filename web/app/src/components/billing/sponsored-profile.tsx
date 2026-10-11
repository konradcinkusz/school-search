import { EnquiryForm } from "@/components/billing/enquiry-form";
import { enquiryConsent, schoolPremium } from "@/lib/billing";
import { SPONSORED_LABEL } from "@/lib/billing-types";
import { safeHttpUrl } from "@/lib/validation";

/**
 * "Profil rozszerzony": a paid, owner-written block shown below the score and the reviews, in its own card.
 * It is labelled "Treść sponsorowana", never changes the score, the ranking, the order or the reviews, and never
 * appears in a list or a comparison. When the Billing service is unavailable or the school has no published
 * profile, nothing is rendered (no empty card, no invented content).
 */
export async function SponsoredProfile({ slug }: { readonly slug: string }) {
  const premium = await schoolPremium(slug);
  if (!premium.ok) return null;
  const { data } = premium;
  if (data.tier !== "premium" || data.profile === null) return null;

  const profile = data.profile;
  const website = safeHttpUrl(profile.websiteUrl);
  const consent = data.enquiriesOpen ? await enquiryConsent() : null;

  return (
    <section aria-labelledby="sponsored-heading" className="sponsored">
      <h2 id="sponsored-heading">Profil rozszerzony szkoły</h2>
      <div className="card sponsored__card">
        <p className="sponsored__label">{SPONSORED_LABEL}</p>
        <p className="muted">
          Tekst przygotowuje szkoła, a jej płatność nie wpływa na wynik, ranking, kolejność ani na opinie użytkowników.
        </p>
        {data.editorsVerified ? (
          <p className="sponsored__verified">Przedstawiciel szkoły potwierdzony przez redakcję.</p>
        ) : null}
        <h3 className="sponsored__headline">{profile.headline}</h3>
        <p className="sponsored__body">{profile.description}</p>
        {website ? (
          <p>
            <a href={website} rel="sponsored nofollow noopener noreferrer">
              Strona szkoły
            </a>
          </p>
        ) : null}
      </div>

      {data.enquiriesOpen ? (
        consent !== null && consent.ok ? (
          <EnquiryForm slug={slug} consentVersion={consent.data.version} consentText={consent.data.text} />
        ) : (
          <p className="muted">Formularz kontaktu jest chwilowo niedostępny.</p>
        )
      ) : null}
    </section>
  );
}
