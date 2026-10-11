import { NextResponse, type NextRequest } from "next/server";
import { clearSessionCookie, ensureFresh, readSession, setSessionCookie } from "@/lib/bff-auth";
import { accountsConfigured, SESSION_COOKIE } from "@/lib/bff-config";
import { accountsUnavailable, bffJson } from "@/lib/bff-http";
import { decodeClaims, emailVerifiedClaim } from "@/lib/jwt-claims";

// BFF: GET /api/auth/session. Returns only {authenticated, emailVerified}. Refreshes the access token when needed.
export const dynamic = "force-dynamic";

export async function GET(request: NextRequest): Promise<NextResponse> {
  if (!accountsConfigured()) return accountsUnavailable();

  const session = readSession(request);
  if (session === null) {
    const response = bffJson(200, { authenticated: false, emailVerified: false });
    if (request.cookies.has(SESSION_COOKIE)) clearSessionCookie(response);
    return response;
  }

  const fresh = await ensureFresh(session);
  if (fresh.kind === "unavailable" || fresh.kind === "disabled") return accountsUnavailable();
  if (fresh.kind === "expired") {
    const response = bffJson(200, { authenticated: false, emailVerified: false });
    clearSessionCookie(response);
    return response;
  }

  const response = bffJson(200, {
    authenticated: true,
    emailVerified: emailVerifiedClaim(decodeClaims(fresh.session.accessToken)),
  });
  if (fresh.rotated) setSessionCookie(response, fresh.session);
  return response;
}
