import { NextResponse, type NextRequest } from "next/server";
import { consentVersions, parseTokenPair, registerAccount, isDisabled } from "@/lib/authservice";
import { isSameOrigin, newSessionFromTokens, setSessionCookie } from "@/lib/bff-auth";
import { accountsConfigured } from "@/lib/bff-config";
import { accountsUnavailable, bffError, bffJson, isRecord, readJsonObject, safeMessages } from "@/lib/bff-http";

// BFF: POST /api/auth/register. Consent versions are fetched from authservice, never supplied by the browser.
export const dynamic = "force-dynamic";

const MAX_BODY_BYTES = 8 * 1024;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const EMAIL_MAX = 200;
const PASSWORD_MIN = 8;
const PASSWORD_MAX = 100;

export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!isSameOrigin(request)) return bffError(403, "forbidden_origin", "Żądanie z tej domeny nie jest dozwolone.");
  if (!accountsConfigured()) return accountsUnavailable();

  const body = await readJsonObject(request, MAX_BODY_BYTES);
  if (body === null) return bffError(400, "bad_request", "Oczekiwano danych w formacie JSON.");

  const email = typeof body.email === "string" ? body.email.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";
  if (email.length > EMAIL_MAX || !EMAIL_PATTERN.test(email)) {
    return bffError(400, "validation_failed", "Podaj poprawny adres e-mail.");
  }
  if (password.length < PASSWORD_MIN || password.length > PASSWORD_MAX) {
    return bffError(400, "validation_failed", `Hasło musi mieć od ${PASSWORD_MIN} do ${PASSWORD_MAX} znaków.`);
  }
  if (body.acceptTerms !== true) {
    return bffError(400, "validation_failed", "Potwierdź akceptację regulaminu i polityki prywatności.");
  }

  const versions = await consentVersions();
  if (versions === null) return accountsUnavailable();

  const outcome = await registerAccount({
    email,
    password,
    acceptedTermsVersion: versions.terms,
    acceptedPrivacyVersion: versions.privacy,
  });
  if (isDisabled(outcome) || outcome.kind === "transient") return accountsUnavailable();

  if (outcome.status === 200) {
    const pair = parseTokenPair(outcome.body);
    if (pair === null) return bffError(502, "upstream_error", "Nie udało się założyć konta. Spróbuj ponownie.");
    const response = bffJson(200, { status: "authenticated" });
    if (!setSessionCookie(response, newSessionFromTokens(pair))) return accountsUnavailable();
    return response;
  }
  if (outcome.status === 202) {
    return bffJson(202, { status: "verification_required" });
  }
  if (outcome.status === 400) {
    const messages = isRecord(outcome.body) ? safeMessages(outcome.body.errors) : [];
    return bffError(400, "validation_failed", "Nie udało się założyć konta.", { messages });
  }
  if (outcome.status === 429) {
    return bffError(429, "rate_limited", "Zbyt wiele prób. Spróbuj ponownie później.");
  }
  return bffError(502, "upstream_error", "Nie udało się założyć konta. Spróbuj ponownie.");
}
