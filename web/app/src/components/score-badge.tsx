import type { ScoreBlock, ScoreStatus } from "@/lib/api";
import { asOfLabel, formatDecimal, formatScore, formatStudents } from "@/lib/format";

/**
 * Renders one score with its interval, cohort size, methodology version and source in the same block.
 * Suppressed states are shown as explicit messages, never as 0 and never as a blank.
 * Scores are never colour-coded as good or bad: every published score uses the same neutral ink.
 * Pure server markup; no hooks.
 */
export type ScoreBadgeVariant = "default" | "compact" | "hero";

/** Source of the score row in the provenance table. Used when the score block carries no source of its own. */
export interface ScoreProvenance {
  readonly source: string;
  readonly asOf: string | null;
}

export interface ScoreBadgeProps {
  readonly status: ScoreStatus;
  readonly block: ScoreBlock | null;
  /** Used only when no block was returned, so the version is still visible. */
  readonly fallbackMethodologyVersion?: string | null;
  /** Used when the block has no source or as-of date, so suppressed states still say where they come from. */
  readonly provenance?: ScoreProvenance | null;
  /** Visual density only. Every variant shows the same facts. */
  readonly variant?: ScoreBadgeVariant;
}

/** Derives the display status for a score block that arrives without an explicit status (ranking items). */
export function scoreStatusOf(block: ScoreBlock | null): ScoreStatus {
  if (block === null) return "no_exam_data";
  if (block.score !== null) return "published";
  if (block.reason === "limited") return "limited";
  return block.reason === "insufficient_cohort" ? "insufficient_cohort" : "no_exam_data";
}

function classes(variant: ScoreBadgeVariant, suppressed: boolean): string {
  const parts = ["score"];
  if (suppressed) parts.push("score--suppressed");
  if (variant !== "default") parts.push(`score--${variant}`);
  return parts.join(" ");
}

interface SourceInfo {
  readonly source: string;
  readonly asOf: string | null;
}

/** The block's own source and date; the provenance row fills in whichever is missing. */
function sourceOf(block: ScoreBlock | null, provenance: ScoreProvenance | null | undefined): SourceInfo {
  const ownSource = typeof block?.source === "string" ? block.source.trim() : "";
  const ownAsOf = typeof block?.asOf === "string" && block.asOf !== "" ? block.asOf : null;
  return {
    source: ownSource !== "" ? ownSource : (provenance?.source?.trim() ?? ""),
    asOf: ownAsOf ?? provenance?.asOf ?? null,
  };
}

/** Source and as-of date of the score. Demo rows are marked so they are never read as real. */
function SourceLine({ info }: { readonly info: SourceInfo }) {
  const asOf = info.asOf ? asOfLabel(info.asOf) : null;
  if (info.source === "" && asOf === null) return null;
  const isDemo = info.source.includes("DEMO SEED");
  return (
    <p className="score__meta score__source">
      {info.source !== "" ? (
        <>
          Źródło: {isDemo ? <span className="badge badge--demo">{info.source}</span> : info.source}
        </>
      ) : null}
      {info.source !== "" && asOf !== null ? " · " : null}
      {asOf !== null ? <span style={{ whiteSpace: "nowrap" }}>{asOf}</span> : null}
    </p>
  );
}

export function ScoreBadge({
  status,
  block,
  fallbackMethodologyVersion,
  provenance,
  variant = "default",
}: ScoreBadgeProps) {
  if (status === "published" && block && block.score !== null) {
    const { score, intervalLow, intervalHigh, cohortSize, methodologyVersion } = block;
    const halfWidth =
      intervalLow !== null && intervalHigh !== null ? (intervalHigh - intervalLow) / 2 : null;
    const intervalText =
      intervalLow !== null && intervalHigh !== null
        ? `przedział 95%: od ${formatScore(intervalLow)} do ${formatScore(intervalHigh)}`
        : "przedział niedostępny";
    return (
      <div className={classes(variant, false)} data-status="published">
        <p className="score__value">
          <span className="score__number">{formatScore(score)}</span>
          {halfWidth !== null ? (
            <span className="score__pm"> ± {formatDecimal(halfWidth)}</span>
          ) : null}
        </p>
        {variant === "compact" ? (
          <p className="score__meta">
            {intervalText} · Kohorta: {formatStudents(cohortSize)} · metodologia {methodologyVersion}
          </p>
        ) : (
          <>
            <p className="score__meta">{intervalText}</p>
            <p className="score__meta">
              Kohorta: {formatStudents(cohortSize)} · metodologia {methodologyVersion}
            </p>
          </>
        )}
        <SourceLine info={sourceOf(block, provenance)} />
      </div>
    );
  }

  // 5 to 8 pupils (ADR-0009): descriptive data and cohort size only. No value, no interval, no rank.
  // The dashed box is its own variant, not the grey suppressed state, so the two read differently.
  if (status === "limited") {
    const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
    return (
      <div className={`${classes(variant, false)} score--limited`} data-status="limited">
        <p className="score__value">Profil ograniczony: mała grupa uczniów</p>
        {block ? <p className="score__meta">Kohorta: {formatStudents(block.cohortSize)}</p> : null}
        <p className="score__meta">Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu.</p>
        {variant !== "compact" ? (
          <p className="score__meta">Średnie wyniki z przedmiotów i liczba uczniów przy każdym przedmiocie są w tabeli poniżej.</p>
        ) : null}
        {version ? <p className="score__meta">metodologia {version}</p> : null}
        <SourceLine info={sourceOf(block, provenance)} />
      </div>
    );
  }

  if (status === "insufficient_cohort") {
    const cohort = block?.cohortSize;
    const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
    return (
      <div className={classes(variant, true)} data-status="insufficient_cohort">
        <p className="score__value">Brak wyniku: zbyt mała grupa</p>
        {cohort !== undefined ? (
          <p className="score__meta">Kohorta: {formatStudents(cohort)}</p>
        ) : null}
        {version ? <p className="score__meta">metodologia {version}</p> : null}
        <p className="score__meta">Brak wyniku nie oznacza zera ani oceny.</p>
        <SourceLine info={sourceOf(block, provenance)} />
      </div>
    );
  }

  if (status === "no_baseline") {
    return (
      <div className={classes(variant, true)} data-status="no_baseline">
        <p className="score__value">Brak wyniku: brak danych porównawczych</p>
        {block ? (
          <p className="score__meta">
            Kohorta: {formatStudents(block.cohortSize)} · metodologia {block.methodologyVersion}
          </p>
        ) : null}
        <SourceLine info={sourceOf(block, provenance)} />
      </div>
    );
  }

  // no_exam_data, or a published status without a usable value: say so, never show a number.
  const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
  return (
    <div className={classes(variant, true)} data-status={status}>
      <p className="score__value">Brak wyniku: brak danych egzaminacyjnych</p>
      {version ? <p className="score__meta">metodologia {version}</p> : null}
      <SourceLine info={sourceOf(block, provenance)} />
    </div>
  );
}
