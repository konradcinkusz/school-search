// Contract types for the Billing service as the web sees it (SPEC-F). Types only: client components import them
// with `import type`. Sponsored content (paid "Profil rozszerzony") is always labelled, shown in its own card and never
// mixed into a score, a ranking, a comparison or any list (SPEC-F hard rules).

export const SPONSORED_LABEL = "Treść sponsorowana";

/** Plan from configuration. priceGrossPln is null until a price is configured (never invented by the web). */
export interface BillingPlan {
  readonly code: string;
  readonly name: string;
  readonly priceGrossPln: number | null;
  readonly interval: string;
}

export interface PlansEnvelope {
  readonly plans: readonly BillingPlan[];
  /** Server text, for example "Cennik w przygotowaniu." */
  readonly note: string | null;
  /** "mock" while sales run in test mode; the page shows the test-mode statement only then. */
  readonly mode: string | null;
}

export interface PremiumProfileView {
  readonly headline: string;
  readonly description: string;
  readonly websiteUrl: string | null;
}

export interface PremiumEnvelope {
  /** Always true. */
  readonly sponsored: true;
  /** none | premium */
  readonly tier: string;
  /** True only after a moderator approved the claim (manual check outside the system). */
  readonly editorsVerified: boolean;
  readonly profile: PremiumProfileView | null;
  readonly label: string;
  readonly enquiriesOpen: boolean;
}

export interface EnquiryConsent {
  readonly version: string;
  readonly text: string;
}

export interface SubscriptionView {
  readonly id: string;
  readonly planCode: string;
  /** pending | active | canceling | expired */
  readonly status: string;
  readonly currentPeriodEnd: string | null;
  readonly cancelAtPeriodEnd: boolean;
}

/** One claim of the signed-in user, with the subscription state of that school when there is one. */
export interface MyClaimView {
  readonly id: string;
  readonly schoolSlug: string;
  readonly roleDescription: string;
  /** pending | approved | rejected | revoked */
  readonly status: string;
  readonly createdAt: string;
  readonly subscription: SubscriptionView | null;
}

/** Enquiry from a parent, visible to the school owner only. Personal data (RODO): never shown publicly. */
export interface LeadView {
  readonly id: string;
  readonly parentEmail: string;
  readonly message: string;
  /** new | read */
  readonly status: string;
  readonly createdAt: string;
  readonly expiresAt: string | null;
}

/** Claim in the moderator queue. The owner hash is never exposed. */
export interface ModerationClaimView {
  readonly id: string;
  readonly schoolSlug: string;
  readonly roleDescription: string;
  /** pending | approved | rejected | revoked */
  readonly status: string;
  readonly createdAt: string;
  readonly notes: string | null;
}

/** Draft suggested by the AI copy assistant. It is never saved or published by the server; the owner edits and saves. */
export interface ProfileDraft {
  readonly headline: string;
  readonly description: string;
}

export interface SimulateEvent {
  readonly subscriptionId: string;
  readonly event: "payment_succeeded" | "subscription_cancelled" | "subscription_ended" | "renewal_failed";
}

/** Limits the web enforces before a request is sent (mirror the server rules). */
export const PROFILE_HEADLINE_MAX = 120;
export const PROFILE_DESCRIPTION_MAX = 1500;
export const PROFILE_URL_MAX = 300;
export const ENQUIRY_MESSAGE_MAX = 1000;
export const ENQUIRY_EMAIL_MAX = 200;
export const DRAFT_BULLETS_MAX = 10;
export const DRAFT_BULLET_MAX = 200;
export const CLAIM_ROLE_MAX = 120;
