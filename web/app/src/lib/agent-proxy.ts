import { NextResponse, type NextRequest } from "next/server";
import { agentBaseUrl, bffSharedSecret } from "@/lib/bff-config";
import { visitorIp } from "@/lib/bff-gate";
import { agentUnavailable, bffError, isRecord } from "@/lib/bff-http";
import { isTeryt } from "@/lib/validation";

// BFF forwarding for the advisor agent (SPEC-G, G6): browser -> /api/agent -> MCP service POST /agent/chat.
// The browser sends only the conversation; the request is rebuilt from validated fields, so no other key reaches
// the agent. The visitor address and the shared secret are forwarded for the agent's per-IP limits.

const AGENT_TIMEOUT_MS = 30_000;
const MAX_TURNS = 12;
const MAX_TURN_CHARS = 1000;

export interface AgentTurnInput {
  readonly role: "user" | "assistant";
  readonly content: string;
}

export interface AgentRequestBody {
  readonly messages: readonly AgentTurnInput[];
  readonly context?: { readonly gmina: string };
}

/** Validates the browser payload. Null when anything is out of contract (the caller answers 400). */
export function parseAgentRequest(raw: Record<string, unknown>): AgentRequestBody | null {
  if (!Array.isArray(raw.messages) || raw.messages.length === 0 || raw.messages.length > MAX_TURNS) return null;
  const messages: AgentTurnInput[] = [];
  for (const item of raw.messages) {
    if (!isRecord(item)) return null;
    if (item.role !== "user" && item.role !== "assistant") return null;
    if (typeof item.content !== "string") return null;
    const content = item.content.trim();
    if (content === "" || content.length > MAX_TURN_CHARS) return null;
    messages.push({ role: item.role, content });
  }
  if (messages[messages.length - 1]?.role !== "user") return null;

  let context: { gmina: string } | undefined;
  if (raw.context !== undefined && raw.context !== null) {
    if (!isRecord(raw.context)) return null;
    if (raw.context.gmina !== undefined) {
      if (typeof raw.context.gmina !== "string" || !isTeryt(raw.context.gmina)) return null;
      context = { gmina: raw.context.gmina };
    }
  }
  return context ? { messages, context } : { messages };
}

/** Forwards one validated conversation. Upstream answers pass through with their status; nothing is cached. */
export async function forwardToAgent(request: NextRequest, body: AgentRequestBody): Promise<NextResponse> {
  const base = agentBaseUrl();
  if (base === null) return agentUnavailable();

  const headers: Record<string, string> = { accept: "application/json", "content-type": "application/json" };
  const secret = bffSharedSecret();
  const clientIp = visitorIp(request);
  if (secret && clientIp) {
    headers["x-client-ip"] = clientIp;
    headers["x-bff-secret"] = secret;
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), AGENT_TIMEOUT_MS);
  try {
    const upstream = await fetch(`${base}/agent/chat`, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
      cache: "no-store",
      signal: controller.signal,
    });
    const text = await upstream.text();
    const responseHeaders = new Headers({
      "cache-control": "no-store",
      "x-content-type-options": "nosniff",
    });
    const contentType = upstream.headers.get("content-type");
    if (contentType) responseHeaders.set("content-type", contentType);
    return new NextResponse(text, { status: upstream.status, headers: responseHeaders });
  } catch (err) {
    const aborted = err instanceof Error && (err.name === "AbortError" || err.name === "TimeoutError");
    if (aborted) return bffError(504, "upstream_timeout", "Doradca nie odpowiedział na czas.");
    console.error("[agent-proxy] upstream failed");
    return agentUnavailable();
  } finally {
    clearTimeout(timer);
  }
}
