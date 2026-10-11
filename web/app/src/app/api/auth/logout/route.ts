import { NextResponse, type NextRequest } from "next/server";
import { logoutAccount } from "@/lib/authservice";
import { clearSessionCookie, isSameOrigin, readSession } from "@/lib/bff-auth";
import { bffError, bffJson } from "@/lib/bff-http";

// BFF: POST /api/auth/logout. Tells authservice first (revokes every refresh token of the user), then clears the cookie.
// The cookie is cleared even when authservice is unreachable, so the browser always ends signed out.
export const dynamic = "force-dynamic";

export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!isSameOrigin(request)) return bffError(403, "forbidden_origin", "Żądanie z tej domeny nie jest dozwolone.");

  const session = readSession(request);
  if (session !== null) {
    await logoutAccount(session.accessToken);
  }
  const response = bffJson(200, { status: "signed_out" });
  clearSessionCookie(response);
  return response;
}
