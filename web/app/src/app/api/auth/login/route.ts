import { NextResponse, type NextRequest } from "next/server";
import { isDisabled, loginAccount, parseTokenPair } from "@/lib/authservice";
import { isSameOrigin, newSessionFromTokens, setSessionCookie } from "@/lib/bff-auth";
import { accountsConfigured } from "@/lib/bff-config";
import { accountsUnavailable, bffError, bffJson, isRecord, readJsonObject } from "@/lib/bff-http";

// BFF: POST /api/auth/login. The password is forwarded to authservice over TLS and never stored.
// Two-factor completion is not implemented yet (AUTHSERVICE-BRIEF G5): the UI is told so explicitly.
export const dynamic = "force-dynamic";

const MAX_BODY_BYTES = 4 * 1024;
const PASSWORD_MAX = 200;
const EMAIL_MAX = 200;

export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!isSameOrigin(request)) return bffError(403, "forbidden_origin", "Żądanie z tej domeny nie jest dozwolone.");
  if (!accountsConfigured()) return accountsUnavailable();

  const body = await readJsonObject(request, MAX_BODY_BYTES);
  if (body === null) return bffError(400, "bad_request", "Oczekiwano danych w formacie JSON.");

  const email = typeof body.email === "string" ? body.email.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";
  if (email === "" || email.length > EMAIL_MAX || password === "" || password.length > PASSWORD_MAX) {
    return bffError(400, "validation_failed", "Podaj adres e-mail i hasło.");
  }

  const outcome = await loginAccount({ email, password });
  if (isDisabled(outcome) || outcome.kind === "transient") return accountsUnavailable();

  if (outcome.status === 200) {
    if (isRecord(outcome.body) && outcome.body.requiresTwoFactor === true) {
      return bffJson(200, { status: "two_factor_required" });
    }
    const pair = parseTokenPair(outcome.body);
    if (pair === null) return bffError(502, "upstream_error", "Nie udało się zalogować. Spróbuj ponownie.");
    const response = bffJson(200, { status: "authenticated" });
    if (!setSessionCookie(response, newSessionFromTokens(pair))) return accountsUnavailable();
    return response;
  }
  if (outcome.status === 403) {
    return bffError(403, "email_verification_required", "Potwierdź adres e-mail, aby się zalogować.");
  }
  if (outcome.status === 401) {
    const locked = isRecord(outcome.body) && outcome.body.lockedOut === true;
    return locked
      ? bffError(401, "locked_out", "Konto jest chwilowo zablokowane po wielu nieudanych próbach.")
      : bffError(401, "invalid_credentials", "Nieprawidłowy adres e-mail lub hasło.");
  }
  if (outcome.status === 429) {
    return bffError(429, "rate_limited", "Zbyt wiele prób logowania. Spróbuj ponownie później.");
  }
  return bffError(502, "upstream_error", "Nie udało się zalogować. Spróbuj ponownie.");
}
