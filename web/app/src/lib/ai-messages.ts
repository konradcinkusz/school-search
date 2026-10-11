// Polish user-facing texts for AI answers. The HTTP status and the uniform error code decide the text; server
// messages are never shown for AI failures (they may be English or describe internals).
import { errorCodeOf, type BffResponse } from "@/lib/bff-client";

export type AiFailure =
  | { readonly kind: "unavailable" }
  | { readonly kind: "rate_limited" }
  | { readonly kind: "rejected" }
  | { readonly kind: "invalid" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "unknown" };

/** Degrade state (P8): AI is off, not configured, or the service did not answer. Everything else keeps working. */
export const AI_UNAVAILABLE_TEXT = "Asystent AI jest teraz niedostępny. Pozostałe funkcje strony działają bez zmian.";

export const AI_FAILURE_TEXT: Record<AiFailure["kind"], string> = {
  unavailable: AI_UNAVAILABLE_TEXT,
  rate_limited: "Wysłano zbyt wiele pytań w krótkim czasie. Spróbuj ponownie za chwilę.",
  rejected: "Odpowiedź nie przeszła sprawdzenia zgodności ze źródłami, dlatego jej nie pokazujemy. Spróbuj inaczej sformułować pytanie.",
  invalid: "Sprawdź pytanie. Jest zbyt krótkie, zbyt długie albo zawiera znaki, których nie możemy przyjąć.",
  forbidden: "Ta funkcja jest dostępna wyłącznie dla moderatorów.",
  unknown: "Nie udało się uzyskać odpowiedzi. Spróbuj ponownie.",
};

export function classifyAiFailure(response: BffResponse | null): AiFailure {
  if (response === null) return { kind: "unavailable" };
  const code = errorCodeOf(response.body);
  if (code === "ai_unavailable" || code === "upstream_unavailable" || code === "agent_unavailable") {
    return { kind: "unavailable" };
  }
  if (code === "rate_limited" || code === "ai_budget_exhausted" || response.status === 429) {
    return { kind: "rate_limited" };
  }
  if (code === "ai_output_rejected" || response.status === 502) return { kind: "rejected" };
  if (response.status === 503 || response.status === 504) return { kind: "unavailable" };
  if (response.status === 401 || response.status === 403) return { kind: "forbidden" };
  if (response.status === 400 || response.status === 413 || response.status === 415 || response.status === 422) {
    return { kind: "invalid" };
  }
  return { kind: "unknown" };
}

export function aiFailureText(response: BffResponse | null): string {
  return AI_FAILURE_TEXT[classifyAiFailure(response).kind];
}

/** Stop reasons from the agent loop, in plain Polish. Unknown values fall back to a neutral sentence. */
export const STOP_REASON_TEXT: Readonly<Record<string, string>> = {
  end_turn: "",
  max_turns: "Doradca osiągnął limit kroków dla jednej odpowiedzi. Zadaj pytanie węziej.",
  max_tool_calls: "Doradca osiągnął limit sprawdzeń dla jednej odpowiedzi. Zadaj pytanie węziej.",
  deadline: "Doradca nie zdążył się odpowiedzieć w czasie. Spróbuj ponownie.",
  budget: "Dzienny limit odpowiedzi AI został wyczerpany. Spróbuj ponownie jutro.",
  ungrounded: "Odpowiedź nie została potwierdzona danymi ze sprawdzeń, dlatego pokazujemy tylko same fakty.",
};

export function stopReasonText(reason: string): string {
  return STOP_REASON_TEXT[reason] ?? "";
}
