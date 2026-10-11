// Contract types for the Reviews service as the web sees it (SPEC-E). Types only: client components import them
// with `import type`, so this module must stay free of server-only imports.
//
// Reviews are user-generated content. They are always shown in their own section, never mixed into a score,
// a ranking or the AI explanation, and never averaged into one number (SPEC-E hard rules).

export const REVIEWS_NOTICE_FALLBACK =
  "Opinie pochodzą od zalogowanych użytkowników z potwierdzonym adresem e-mail. Nie weryfikujemy, czy autor jest rodzicem ucznia tej szkoły. Opinie nie wpływają na wynik szkoły.";

export type ReplyVerificationState = "unverified" | "verified_by_moderator";

export interface ReviewReplyItem {
  readonly body: string;
  readonly representativeLabel: string;
  readonly verificationState: ReplyVerificationState | string;
}

export interface ReviewItem {
  readonly id: string;
  /** Individual rating of one reviewer (1..5). Never aggregated. */
  readonly rating: number;
  readonly title: string;
  readonly body: string;
  /** "2025/26" */
  readonly schoolYear: string;
  /** "październik 2026" style label from the server. */
  readonly reviewedMonth: string;
  /** Always "Rodzic/opiekun (niezweryfikowany)". */
  readonly authorLabel: string;
  readonly reply: ReviewReplyItem | null;
}

export interface ReviewsEnvelope {
  /** Always true. */
  readonly userGenerated: true;
  readonly notice: string;
  readonly items: readonly ReviewItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export type DecisionAction = "remove" | "restrict" | "keep" | "publish" | "reject" | string;

/** Statement of reasons (DSA art. 17). Shown to the notifier and to the affected author. */
export interface StatementOfReasons {
  readonly id: string;
  readonly action: DecisionAction;
  readonly legalOrTermsGround: string;
  readonly facts: string;
  /** Always false: no automated decision is ever taken. */
  readonly automated: boolean;
  readonly redressInfo: string;
  readonly decidedAt: string;
}

export interface MyReview {
  readonly id: string;
  readonly schoolSlug: string;
  readonly rating: number;
  readonly title: string;
  readonly body: string;
  readonly schoolYearFrom: number;
  /** pending | published | rejected | removed */
  readonly status: string;
  readonly createdAt: string;
  readonly decision: StatementOfReasons | null;
}

export interface LegalTerms {
  /** Version the browser must send as acceptedTermsVersion when writing a review. */
  readonly version: string;
}

export interface LegalContact {
  readonly contactEmail: string | null;
  readonly contactUrl: string | null;
}

export interface NoticeCreated {
  readonly id: string;
  readonly ackToken: string;
  readonly receivedAt: string;
}

export interface NoticeStatusView {
  readonly id: string;
  /** received | decided */
  readonly status: string;
  readonly receivedAt: string;
  readonly decision: (StatementOfReasons & { readonly id: string }) | null;
}

export interface ModerationQueueItem {
  /** review | reply | notice | complaint */
  readonly kind: string;
  readonly id: string;
  readonly status: string;
  /** ok | needs_attention | likely_personal_data | likely_abusive | unclear. Advisory only. */
  readonly triageLabel: string | null;
  readonly title: string | null;
  readonly body: string | null;
  readonly contentReference: string | null;
  readonly explanation: string | null;
}

export interface ModerationQueue {
  readonly items: readonly ModerationQueueItem[];
}
