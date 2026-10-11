import { NextResponse, type NextRequest } from "next/server";
import { clearSessionCookie, ensureFresh, readSession, setSessionCookie } from "@/lib/bff-auth";
import { accountsConfigured } from "@/lib/bff-config";
import { accountsUnavailable, bffError } from "@/lib/bff-http";
import type { SessionPayload } from "@/lib/session";

// Access gate shared by the BFF forwarders (Reviews, Billing). Each service validates its own tokens; this gate
// only decides whether a browser request carries a usable session for the route's access level.

/**
 * anonymous: no session needed.
 * optional: a session is attached when present.
 * required: a session is needed; the service itself enforces email verification and ownership.
 * moderator: a session plus a moderator role claim. Gating only: the service re-checks every call.
 */
export type ServiceAccess = "anonymous" | "optional" | "required" | "moderator";

/** Session failures for a required or moderator route. Returns a response to send, or the session to use. */
export type Resolved =
  | { readonly kind: "anonymous" }
  | { readonly kind: "session"; readonly accessToken: string; readonly rotated: SessionRotation }
  | { readonly kind: "respond"; readonly response: NextResponse };

export type SessionRotation = { readonly payload: SessionPayload } | null;

export async function resolveSession(request: NextRequest, access: ServiceAccess): Promise<Resolved> {
  if (access === "anonymous") return { kind: "anonymous" };

  if (!accountsConfigured()) {
    if (access === "optional") return { kind: "anonymous" };
    return { kind: "respond", response: accountsUnavailable() };
  }

  const session = readSession(request);
  if (session === null) {
    if (access === "optional") return { kind: "anonymous" };
    return {
      kind: "respond",
      response: bffError(401, "unauthenticated", "Zaloguj się, aby kontynuować."),
    };
  }

  const fresh = await ensureFresh(session);
  if (fresh.kind === "fresh") {
    return {
      kind: "session",
      accessToken: fresh.session.accessToken,
      rotated: fresh.rotated ? { payload: fresh.session } : null,
    };
  }
  if (access === "optional") return { kind: "anonymous" };
  if (fresh.kind === "expired") {
    const response = bffError(401, "unauthenticated", "Sesja wygasła. Zaloguj się ponownie.");
    clearSessionCookie(response);
    return { kind: "respond", response };
  }
  return { kind: "respond", response: accountsUnavailable() };
}

export function withRotatedCookie(response: NextResponse, rotated: SessionRotation): NextResponse {
  if (rotated) setSessionCookie(response, rotated.payload);
  return response;
}

/** Client address for upstream rate limits: first hop of fly-client-ip or x-forwarded-for, when well-formed. */
export function visitorIp(request: NextRequest): string | null {
  const raw = (request.headers.get("fly-client-ip") ?? request.headers.get("x-forwarded-for") ?? "").split(",")[0]?.trim();
  return raw && /^[0-9a-fA-F:.]{3,45}$/.test(raw) ? raw : null;
}
