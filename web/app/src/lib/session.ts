import { createCipheriv, createDecipheriv, createHash, hkdfSync, randomBytes, randomUUID } from "node:crypto";

// Sealed BFF session (SPEC-E §E3). Server-only: the key never leaves the process and the access token never reaches
// browser JavaScript. Format: "v1." + base64url(iv[12] | tag[16] | ciphertext), AES-256-GCM. The key is derived from
// SESSION_SECRET with HKDF-SHA256, so the raw secret is never used directly as a cipher key.

const VERSION_PREFIX = "v1.";
const IV_BYTES = 12;
const TAG_BYTES = 16;
const HKDF_SALT = "school-search/bff-session";
const HKDF_INFO = "ss_session v1";

/** Server-side session state. It lives only inside the sealed cookie. */
export interface SessionPayload {
  /** Stable id for this login. Survives token rotation; keys the single-flight refresh. */
  readonly sid: string;
  readonly accessToken: string;
  readonly refreshToken: string;
  /** Epoch milliseconds when the access token expires. */
  readonly expiresAt: number;
  /** Masked address for display, for example "k***@example.pl". Never the full address. */
  readonly userEmailMasked: string;
}

export function deriveSessionKey(secret: string): Buffer {
  return Buffer.from(hkdfSync("sha256", Buffer.from(secret, "utf8"), HKDF_SALT, HKDF_INFO, 32));
}

export function sealSession(payload: SessionPayload, key: Buffer): string {
  const iv = randomBytes(IV_BYTES);
  const cipher = createCipheriv("aes-256-gcm", key, iv, { authTagLength: TAG_BYTES });
  const plaintext = Buffer.from(JSON.stringify(payload), "utf8");
  const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
  const tag = cipher.getAuthTag();
  return VERSION_PREFIX + Buffer.concat([iv, tag, ciphertext]).toString("base64url");
}

function isSessionPayload(value: unknown): value is SessionPayload {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.sid === "string" &&
    typeof v.accessToken === "string" &&
    typeof v.refreshToken === "string" &&
    typeof v.expiresAt === "number" &&
    Number.isFinite(v.expiresAt) &&
    typeof v.userEmailMasked === "string"
  );
}

/** Returns the payload, or null for anything tampered, truncated, foreign or undecryptable. Never throws. */
export function unsealSession(sealed: string | undefined, key: Buffer): SessionPayload | null {
  if (sealed === undefined || !sealed.startsWith(VERSION_PREFIX)) return null;
  try {
    const raw = Buffer.from(sealed.slice(VERSION_PREFIX.length), "base64url");
    if (raw.length <= IV_BYTES + TAG_BYTES) return null;
    const iv = raw.subarray(0, IV_BYTES);
    const tag = raw.subarray(IV_BYTES, IV_BYTES + TAG_BYTES);
    const ciphertext = raw.subarray(IV_BYTES + TAG_BYTES);
    const decipher = createDecipheriv("aes-256-gcm", key, iv, { authTagLength: TAG_BYTES });
    decipher.setAuthTag(tag);
    const plaintext = Buffer.concat([decipher.update(ciphertext), decipher.final()]).toString("utf8");
    const parsed: unknown = JSON.parse(plaintext);
    return isSessionPayload(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export function newSessionId(): string {
  return randomUUID();
}

/** Stable, non-reversible key for an opaque token, used for in-memory maps (never logged, never sent). */
export function tokenFingerprint(token: string): string {
  return createHash("sha256").update(token, "utf8").digest("base64url");
}

/** "k***@example.pl" from "konrad@example.pl". Anything without an @ becomes a fixed mask. */
export function maskEmail(email: string): string {
  const at = email.lastIndexOf("@");
  if (at <= 0 || at === email.length - 1) return "***";
  return `${email.charAt(0)}***${email.slice(at)}`;
}
