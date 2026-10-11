/**
 * Reads claims from an access token this BFF received directly from authservice over TLS.
 * No signature check here: the token is never accepted from a browser, and every service that receives it
 * validates it again through authservice discovery and JWKS. Used only for UX gating and display.
 *
 * Claim wire names are UNVERIFIED (AUTHSERVICE-BRIEF §1): the role may arrive as "role" or as the long
 * .NET claim type, so both are accepted.
 */

const ROLE_CLAIMS = ["role", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] as const;
const MODERATOR_ROLES: ReadonlySet<string> = new Set(["Admin", "SuperAdmin"]);

export type Claims = Readonly<Record<string, unknown>>;

export function decodeClaims(accessToken: string): Claims | null {
  const parts = accessToken.split(".");
  if (parts.length !== 3 || parts[1] === undefined || parts[1] === "") return null;
  try {
    const parsed: unknown = JSON.parse(Buffer.from(parts[1], "base64url").toString("utf8"));
    return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed) ? (parsed as Claims) : null;
  } catch {
    return null;
  }
}

export function emailVerifiedClaim(claims: Claims | null): boolean {
  return claims !== null && claims.email_verified === true;
}

/** Moderator = role Admin or SuperAdmin (SPEC-E). Gating only: the Reviews service re-checks every request. */
export function hasModeratorRole(claims: Claims | null): boolean {
  if (claims === null) return false;
  for (const name of ROLE_CLAIMS) {
    const value = claims[name];
    const values = Array.isArray(value) ? value : [value];
    if (values.some((item) => typeof item === "string" && MODERATOR_ROLES.has(item))) return true;
  }
  return false;
}

export function emailClaim(claims: Claims | null): string | null {
  return claims !== null && typeof claims.email === "string" ? claims.email : null;
}
