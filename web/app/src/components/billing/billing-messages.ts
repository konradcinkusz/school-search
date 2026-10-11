// Polish user-facing texts for Billing answers. The status and the uniform error code decide the text; server
// messages are English and are only shown where the code alone cannot say what went wrong (linter, draft).
import { errorCodeOf, errorMessageOf, type BffResponse } from "@/lib/bff-client";

const CODE_TEXT: Record<string, string> = {
  unauthenticated: "Sesja wygasła. Zaloguj się ponownie.",
  unauthorized: "Sesja wygasła. Zaloguj się ponownie.",
  email_not_verified: "Potwierdź adres e-mail, aby korzystać z konta szkoły.",
  forbidden_role: "Ta strona jest dostępna wyłącznie dla moderatorów.",
  forbidden: "Ta operacja nie jest dostępna.",
  auth_unavailable: "Konta są chwilowo niedostępne.",
  billing_unavailable: "Płatne profile są chwilowo niedostępne.",
  upstream_timeout: "Usługa nie odpowiedziała na czas. Spróbuj ponownie.",
  rate_limited: "Zbyt wiele prób w krótkim czasie. Spróbuj ponownie później.",
  not_found: "Nie znaleziono.",
  claim_not_allowed: "Tekst zawiera sformułowanie, którego nie można użyć w profilu szkoły.",
  draft_rejected: "Propozycja tekstu nie spełniła zasad profilu. Napisz tekst samodzielnie.",
  ai_unavailable: "Asystent tekstu jest chwilowo niedostępny. Napisz tekst samodzielnie.",
  validation_failed: "Sprawdź dane formularza. Pola muszą spełniać podane wymagania.",
};

/** Codes whose server message names the reason (for example the matched linter term). */
const SHOW_SERVER_MESSAGE = new Set(["claim_not_allowed", "draft_rejected"]);

export interface BillingFeedback {
  readonly text: string;
  readonly detail: string | null;
}

export function billingFeedback(response: BffResponse | null, fallback: string): BillingFeedback {
  if (response === null) return { text: "Nie udało się połączyć z usługą. Spróbuj ponownie.", detail: null };
  const code = errorCodeOf(response.body);
  if (response.status === 503 && code === null) return { text: CODE_TEXT.billing_unavailable ?? fallback, detail: null };
  if (response.status === 401 && code === null) return { text: CODE_TEXT.unauthenticated ?? fallback, detail: null };
  if (response.status === 429 && code === null) return { text: CODE_TEXT.rate_limited ?? fallback, detail: null };
  const known = code !== null ? CODE_TEXT[code] : undefined;
  const text = known ?? fallback;
  const detail = code !== null && SHOW_SERVER_MESSAGE.has(code) ? errorMessageOf(response.body) : null;
  return { text, detail };
}

export const CLAIM_STATUS_LABEL: Record<string, string> = {
  pending: "oczekuje na sprawdzenie przez redakcję",
  approved: "zatwierdzone przez redakcję",
  rejected: "odrzucone",
  revoked: "cofnięte",
};

export const SUBSCRIPTION_STATUS_LABEL: Record<string, string> = {
  pending: "płatność w toku",
  active: "aktywna",
  canceling: "aktywna, zakończy się na koniec okresu",
  expired: "wygasła (profil zablokowany, dane zachowane)",
};

export const INTERVAL_LABEL: Record<string, string> = {
  month: "miesiąc",
  year: "rok",
};
