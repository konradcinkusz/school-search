"use client";

import Link from "next/link";
import { useId, useState } from "react";
import { ScoreBadge } from "@/components/score-badge";
import type { ExplainErrorCode, ExplainFacts, ExplainResponse, ExplainResult, ScoreBlock, ScoreStatus } from "@/lib/api";
import { formatDatePl, formatPercent } from "@/lib/format";

/**
 * "Wyjaśnij ten wynik": an AI explanation of the score already shown on the profile.
 * Posts through the BFF proxy only (never to the catalog). The AI text is plain text, rendered by React escaping;
 * it is never parsed as HTML or Markdown. The structured facts are rendered next to it by the same ScoreBadge
 * as the score section, so the number is never taken from the model.
 */

const EXPLAIN_TIMEOUT_MS = 60_000;
const LABEL = "Wygenerowano przez AI — to nie jest ocena szkoły";
const UNAVAILABLE = "Wyjaśnienie AI jest teraz niedostępne. Wynik i metodologia powyżej są kompletne.";

const ERROR_TEXT: Record<ExplainErrorCode, string> = {
  ai_unavailable: UNAVAILABLE,
  ai_budget_exhausted:
    "Dzienny limit wyjaśnień AI został wyczerpany. Spróbuj ponownie jutro. Wynik i metodologia powyżej są kompletne.",
  ai_output_rejected: UNAVAILABLE,
  rate_limited: "Wysłano zbyt wiele próśb o wyjaśnienie. Spróbuj ponownie później.",
  upstream_unavailable: UNAVAILABLE,
  upstream_timeout: UNAVAILABLE,
  unknown: UNAVAILABLE,
};

const KNOWN_CODES: ReadonlySet<string> = new Set<ExplainErrorCode>([
  "ai_unavailable",
  "ai_budget_exhausted",
  "ai_output_rejected",
  "rate_limited",
  "upstream_unavailable",
  "upstream_timeout",
]);

const SCORE_STATUSES: ReadonlySet<string> = new Set<ScoreStatus>([
  "published",
  "limited",
  "insufficient_cohort",
  "no_exam_data",
  "no_baseline",
]);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isOptionalNumber(value: unknown): value is number | null {
  return value === null || typeof value === "number";
}

function isScoreBlock(value: unknown): value is ScoreBlock {
  return (
    isRecord(value) &&
    isOptionalNumber(value.score) &&
    isOptionalNumber(value.intervalLow) &&
    isOptionalNumber(value.intervalHigh) &&
    typeof value.cohortSize === "number" &&
    typeof value.methodologyVersion === "string" &&
    typeof value.examYear === "number" &&
    typeof value.asOf === "string"
  );
}

/** Accepts only the documented explain shape; anything else is treated as a failed response. */
function parseExplain(payload: unknown): ExplainResponse | null {
  if (!isRecord(payload)) return null;
  const { slug, text, label, model, methodologyVersion, examYear, facts, generatedAt, cached } = payload;
  if (
    typeof slug !== "string" ||
    typeof text !== "string" ||
    text.trim() === "" ||
    typeof label !== "string" ||
    typeof model !== "string" ||
    typeof methodologyVersion !== "string" ||
    typeof examYear !== "number" ||
    typeof generatedAt !== "string" ||
    typeof cached !== "boolean" ||
    !isRecord(facts) ||
    typeof facts.scoreStatus !== "string" ||
    !SCORE_STATUSES.has(facts.scoreStatus)
  ) {
    return null;
  }
  if (facts.score !== null && !isScoreBlock(facts.score)) return null;
  if (!isOptionalNumber(facts.coveragePercent) || !isOptionalNumber(facts.cohortSize)) return null;

  const parsedFacts: ExplainFacts = {
    score: facts.score as ScoreBlock | null,
    scoreStatus: facts.scoreStatus as ScoreStatus,
    coveragePercent: facts.coveragePercent,
    cohortSize: facts.cohortSize,
  };
  return {
    slug,
    text,
    label,
    model,
    methodologyVersion,
    examYear,
    facts: parsedFacts,
    generatedAt,
    cached,
  };
}

function errorCodeFor(status: number, payload: unknown): ExplainErrorCode {
  if (isRecord(payload) && isRecord(payload.error) && typeof payload.error.code === "string") {
    if (KNOWN_CODES.has(payload.error.code)) return payload.error.code as ExplainErrorCode;
  }
  if (status === 429) return "rate_limited";
  if (status === 503) return "ai_unavailable";
  if (status === 502) return "ai_output_rejected";
  if (status === 504) return "upstream_timeout";
  return "unknown";
}

async function requestExplanation(slug: string): Promise<ExplainResult> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), EXPLAIN_TIMEOUT_MS);
  try {
    const response = await fetch(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/explain`, {
      method: "POST",
      headers: { accept: "application/json" },
      cache: "no-store",
      signal: controller.signal,
    });
    const payload: unknown = await response.json().catch(() => null);
    if (response.status === 200) {
      const data = parseExplain(payload);
      return data ? { ok: true, data } : { ok: false, status: 200, code: "unknown" };
    }
    return { ok: false, status: response.status, code: errorCodeFor(response.status, payload) };
  } catch {
    return { ok: false, status: 0, code: controller.signal.aborted ? "upstream_timeout" : "upstream_unavailable" };
  } finally {
    clearTimeout(timer);
  }
}

export function ExplainPanel({ slug }: { readonly slug: string }) {
  const ids = {
    heading: useId(),
    status: useId(),
  };
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<ExplainResult | null>(null);

  const explanation = result !== null && result.ok ? result.data : null;
  const failure = result !== null && !result.ok ? result : null;

  async function onExplain() {
    setLoading(true);
    setResult(null);
    const outcome = await requestExplanation(slug);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className="card ai-explain ai-panel" aria-labelledby={ids.heading}>
      <div className="ai-panel__head">
        <h2 id={ids.heading}>Wyjaśnienie wyniku</h2>
        <span className="badge badge--ai" aria-hidden="true">
          AI
        </span>
      </div>
      <p className="muted">
        Krótki tekst wygenerowany przez AI na podstawie danych z tej strony. Nie zastępuje wyniku ani metodologii.
      </p>
      <button type="button" className="button button--secondary" onClick={onExplain} disabled={loading || explanation !== null} aria-describedby={ids.status}>
        Wyjaśnij ten wynik
      </button>
      <div id={ids.status} role="status" aria-live="polite">
        {loading ? <p className="muted">Generowanie wyjaśnienia…</p> : null}
      </div>

      {failure ? (
        <div className="notice" role="alert">
          {ERROR_TEXT[failure.code]}
        </div>
      ) : null}

      {explanation ? <ExplanationOutput data={explanation} /> : null}
    </section>
  );
}

function ExplanationOutput({ data }: { readonly data: ExplainResponse }) {
  const labelId = useId();
  const generated = formatDatePl(data.generatedAt);
  const { facts } = data;
  return (
    <div className="ai-explain__result">
      <section className="ai-box" aria-labelledby={labelId}>
        <p id={labelId} className="ai-box__label">
          {LABEL}
        </p>
        {/* Plain text: React escapes it. Line breaks are kept by CSS (white-space: pre-line). */}
        <p className="ai-box__text">{data.text}</p>
        <p className="muted ai-box__meta">
          Model {data.model} · rok egzaminu {data.examYear} · metodologia {data.methodologyVersion}
          {generated ? ` · wygenerowano ${generated}` : ""}
          {data.cached ? " · z pamięci podręcznej" : ""}
        </p>
      </section>

      <div className="ai-explain__facts">
        <ScoreBadge
          status={facts.scoreStatus}
          block={facts.score}
          fallbackMethodologyVersion={data.methodologyVersion}
        />
        {facts.coveragePercent !== null ? (
          <p className="score__meta">Udział w egzaminie: {formatPercent(facts.coveragePercent)}</p>
        ) : null}
      </div>

      <p className="muted">
        Zasady liczenia i ograniczenia opisuje <Link href="/metodologia">metodologia</Link>.
      </p>
    </div>
  );
}
