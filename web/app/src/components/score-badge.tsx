import type { ScoreBlock, ScoreStatus } from "@/lib/api";
import { formatDecimal, formatScore, formatStudents } from "@/lib/format";

/**
 * Renders one score with its interval, cohort size and methodology version in the same block.
 * Suppressed states are shown as explicit messages, never as 0 and never as a blank.
 * Pure server markup; no hooks.
 */
export interface ScoreBadgeProps {
  readonly status: ScoreStatus;
  readonly block: ScoreBlock | null;
  /** Used only when no block was returned, so the version is still visible. */
  readonly fallbackMethodologyVersion?: string | null;
}

/** Derives the display status for a score block that arrives without an explicit status (ranking items). */
export function scoreStatusOf(block: ScoreBlock | null): ScoreStatus {
  if (block === null) return "no_exam_data";
  if (block.score !== null) return "published";
  return block.reason === "insufficient_cohort" ? "insufficient_cohort" : "no_exam_data";
}

export function ScoreBadge({ status, block, fallbackMethodologyVersion }: ScoreBadgeProps) {
  if (status === "published" && block && block.score !== null) {
    const { score, intervalLow, intervalHigh, cohortSize, methodologyVersion } = block;
    const halfWidth =
      intervalLow !== null && intervalHigh !== null ? (intervalHigh - intervalLow) / 2 : null;
    return (
      <div className="score" data-status="published">
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
      </div>
    );
  }

  if (status === "insufficient_cohort") {
    const cohort = block?.cohortSize;
    const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
    return (
      <div className="score score--suppressed" data-status="insufficient_cohort">
        <p className="score__value">Brak wyniku: zbyt mała grupa (insufficient_cohort)</p>
        {cohort !== undefined ? (
          <p className="score__meta">Kohorta: {formatStudents(cohort)}</p>
        ) : null}
        {version ? <p className="score__meta">metodologia {version}</p> : null}
        <p className="score__meta">Brak wyniku nie oznacza zera ani oceny.</p>
      </div>
    );
  }

  if (status === "no_baseline") {
    return (
      <div className="score score--suppressed" data-status="no_baseline">
        <p className="score__value">Brak wyniku: brak danych porównawczych (no_baseline)</p>
        {block ? (
          <p className="score__meta">
            Kohorta: {formatStudents(block.cohortSize)} · metodologia {block.methodologyVersion}
          </p>
        ) : null}
      </div>
    );
  }

  // no_exam_data, or a published status without a usable value: say so, never show a number.
  const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
  return (
    <div className="score score--suppressed" data-status={status}>
      <p className="score__value">Brak wyniku: brak danych egzaminacyjnych (no_exam_data)</p>
      {version ? <p className="score__meta">metodologia {version}</p> : null}
    </div>
  );
}
