import { NextResponse, type NextRequest } from "next/server";
import { parseAgentRequest, forwardToAgent } from "@/lib/agent-proxy";
import { bffError, readJsonObject } from "@/lib/bff-http";
import { isSameOrigin } from "@/lib/bff-auth";

// Advisor chat endpoint (SPEC-G, G6). Same-origin only: a foreign Origin, a missing Origin or a non-JSON body is refused.
// Anonymous by design (the advisor needs no account); the agent's own per-IP and token limits apply upstream.
export const dynamic = "force-dynamic";

const MAX_BODY_BYTES = 24 * 1024;

export async function POST(request: NextRequest): Promise<NextResponse> {
  if (!isSameOrigin(request)) {
    return bffError(403, "forbidden_origin", "Żądanie z tej domeny nie jest dozwolone.");
  }
  const raw = await readJsonObject(request, MAX_BODY_BYTES);
  if (raw === null) {
    return bffError(400, "validation_failed", "Oczekiwano poprawnej rozmowy w formacie JSON.");
  }
  const body = parseAgentRequest(raw);
  if (body === null) {
    return bffError(
      400,
      "validation_failed",
      "Rozmowa musi mieć od 1 do 12 wiadomości, każda do 1000 znaków, a ostatnia od użytkownika.",
    );
  }
  return forwardToAgent(request, body);
}
