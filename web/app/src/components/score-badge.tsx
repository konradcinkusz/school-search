import type { ScoreBlock, ScoreStatus } from "@/lib/api";
import { asOfLabel, formatDecimal, formatScore, formatStudents } from "@/lib/format";

/**
 * Renders one score with its interval, cohort size, methodology version and source in the same block.
 * Suppressed states are shown as explicit messages, never as 0 and never as a blank.
 * Scores are never colour-coded as good or bad: every published score uses the same neutral ink.
 * Pure server markup; no hooks.
 */
export type ScoreBadgeVariant = "default" | "compact" | "hero";

export interface ScoreBadgeProps {
  readonly status: ScoreStatus;
  readonly block: ScoreBlock | null;
  /** Used only when no block was returned, so the version is still visible. */
  readonly fallbackMethodologyVersion?: string | null;
  /** Visual density only. Every variant shows the same facts. */
  readonly variant?: ScoreBadgeVariant;
}

/** Derives the display status for a score block that arrives without an explicit status (ranking items). */
export function scoreStatusOf(block: ScoreBlock | null): ScoreStatus {
  if (block === null) return "no_exam_data";
  if (block.score !== null) return "published";
  return block.reason === "insufficient_cohort" ? "insufficient_cohort" : "no_exam_data";
}

function classes(variant: ScoreBadgeVariant, suppressed: boolean): string {
  const parts = ["score"];
  if (suppressed) parts.push("score--suppressed");
  if (variant !== "default") parts.push(`score--${variant}`);
  return parts.join(" ");
}

/** Source and as-of date of the score, when the API sent them. Demo rows are marked so they are never read as real. */
function SourceLine({ block }: { readonly block: ScoreBlock | null }) {
  if (block === null) return null;
  const source = typeof block.source === "string" ? block.source.trim() : "";
  const asOf = typeof block.asOf === "string" && block.asOf !== "" ? asOfLabel(block.asOf) : null;
  if (source === "" && asOf === null) return null;
  const isDemo = source.includes("DEMO SEED");
  return (
    <p className="score__meta score__source">
      {source !== "" ? (
        <>
          Źródło: {isDemo ? <span className="badge badge--demo">{source}</span> : source}
        </>
      ) : null}
      {source !== "" && asOf !== null ? " · " : null}
      {asOf}
    </p>
  );
}

export function ScoreBadge({ status, block, fallbackMethodologyVersion, variant = "default" }: ScoreBadgeProps) {
  if (status === "published" && block && block.score !== null) {
    const { score, intervalLow, intervalHigh, cohortSize, methodologyVersion } = block;
    const halfWidth =
      intervalLow !== null && intervalHigh !== null ? (intervalHigh - intervalLow) / 2 : null;
    return (
      <div className={classes(variant, false)} data-status="published">
        <p className="score__value">
          <span className="score__number">{formatScore(score)}</span>
          {halfWidth !== null ? (
            <span className="score__pm"> ± {formatDecimal(halfWidth)}</span>
          ) : null}
        </p>
        {intervalLow !== null && intervalHigh !== null ? (
          <p className="score__meta">
            przedział 95%: od {formatScore(intervalLow)} do {formatScore(intervalHigh)}
          </p>
        ) : (
          <p className="score__meta">przedział niedostępny</p>
        )}
        <p className="score__meta">
          Kohorta: {formatStudents(cohortSize)} · metodologia {methodologyVersion}
        </p>
        <SourceLine block={block} />
      </div>
    );
  }

  if (status === "insufficient_cohort") {
    const cohort = block?.cohortSize;
    const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
    return (
      <div className={classes(variant, true)} data-status="insufficient_cohort">
        <p className="score__value">Brak wyniku: zbyt mała grupa (insufficient_cohort)</p>
        {cohort !== undefined ? (
          <p className="score__meta">Kohorta: {formatStudents(cohort)}</p>
        ) : null}
        {version ? <p className="score__meta">metodologia {version}</p> : null}
        <p className="score__meta">Brak wyniku nie oznacza zera ani oceny.</p>
        <SourceLine block={block} />
      </div>
    );
  }

  if (status === "no_baseline") {
    return (
      <div className={classes(variant, true)} data-status="no_baseline">
        <p className="score__value">Brak wyniku: brak danych porównawczych (no_baseline)</p>
        {block ? (
          <p className="score__meta">
            Kohorta: {formatStudents(block.cohortSize)} · metodologia {block.methodologyVersion}
          </p>
        ) : null}
        <SourceLine block={block} />
      </div>
    );
  }

  // no_exam_data, or a published status without a usable value: say so, never show a number.
  const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
  return (
    <div className={classes(variant, true)} data-status={status}>
      <p className="score__value">Brak wyniku: brak danych egzaminacyjnych (no_exam_data)</p>
      {version ? <p className="score__meta">metodologia {version}</p> : null}
    </div>
  );
}
