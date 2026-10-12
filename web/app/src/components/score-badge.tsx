import Link from "next/link";
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

/**
 * Source and as-of date of the score. Demo rows are marked so they are never read as real.
 * The separator sits inside the same no-break span as the date, so a line never ends with a dangling "·".
 */
function SourceLine({ info }: { readonly info: SourceInfo }) {
  const asOf = info.asOf ? asOfLabel(info.asOf) : null;
  const hasSource = info.source !== "";
  if (!hasSource && asOf === null) return null;
  const isDemo = info.source.includes("DEMO SEED");
  return (
    <p className="score__meta score__source">
      {hasSource ? (
        <>
          Źródło: {isDemo ? <span className="badge badge--demo">{info.source}</span> : info.source}
        </>
      ) : null}
      {hasSource && asOf !== null ? " " : null}
      {asOf !== null ? <span className="nowrap">{hasSource ? `· ${asOf}` : asOf}</span> : null}
    </p>
  );
}

/** Cohort size in plain Polish: the pupils the score is calculated from. */
function CohortLine({ cohortSize }: { readonly cohortSize: number }) {
  return <>Grupa porównawcza: {formatStudents(cohortSize)}</>;
}

/** Methodology version as a small link to the explanation, so the term is read in context. */
function MethodologyLine({ version }: { readonly version: string }) {
  return (
    <p className="score__meta score__meta--method">
      <Link href="/metodologia">Wersja metodologii {version}</Link>
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
    const hasInterval = intervalLow !== null && intervalHigh !== null;
    const halfWidth = hasInterval ? (intervalHigh - intervalLow) / 2 : null;
    const intervalText = hasInterval
      ? `przedział 95%: od ${formatScore(intervalLow)} do ${formatScore(intervalHigh)}`
      : "przedział niedostępny";
    return (
      <div className={classes(variant, false)} data-status="published">
        <div className="score__lead">
          <p className="score__value">
            <span className="score__number">{formatScore(score)}</span>
            {halfWidth !== null ? <span className="score__pm"> ± {formatDecimal(halfWidth)}</span> : null}
          </p>
          {variant === "hero" && hasInterval ? (
            // Decorative picture of the interval: the same two numbers as the text line below, never a scale.
            <div className="score__range" aria-hidden="true">
              <span className="score__range-end">{formatScore(intervalLow)}</span>
              <span className="score__range-bar">
                <span className="score__range-dot" />
              </span>
              <span className="score__range-end">{formatScore(intervalHigh)}</span>
            </div>
          ) : null}
        </div>
        {variant === "compact" ? (
          <div className="score__facts">
            <p className="score__meta">
              {intervalText} · <CohortLine cohortSize={cohortSize} /> ·{" "}
              <Link href="/metodologia">wersja {methodologyVersion}</Link>
            </p>
            <SourceLine info={sourceOf(block, provenance)} />
          </div>
        ) : (
          <div className="score__facts">
            <p className="score__meta score__meta--interval">{intervalText}</p>
            <p className="score__meta score__meta--cohort">
              <CohortLine cohortSize={cohortSize} />
            </p>
            <MethodologyLine version={methodologyVersion} />
            <SourceLine info={sourceOf(block, provenance)} />
          </div>
        )}
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
        {block ? (
          <p className="score__meta score__meta--cohort">
            <CohortLine cohortSize={block.cohortSize} />
          </p>
        ) : null}
        <p className="score__meta score__meta--strong">Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu.</p>
        {variant !== "compact" ? (
          <>
            <p className="score__meta">
              Powód: w tej grupie przystąpiło do egzaminu zbyt mało uczniów, by wynik był wiarygodny. To nie jest ocena
              szkoły.
            </p>
            <p className="score__meta">Średnie wyniki z przedmiotów i liczba uczniów przy każdym przedmiocie są w tabeli poniżej.</p>
          </>
        ) : null}
        {version ? <MethodologyLine version={version} /> : null}
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
          <p className="score__meta score__meta--cohort">
            <CohortLine cohortSize={cohort} />
          </p>
        ) : null}
        {version ? <MethodologyLine version={version} /> : null}
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
          <>
            <p className="score__meta score__meta--cohort">
              <CohortLine cohortSize={block.cohortSize} />
            </p>
            <MethodologyLine version={block.methodologyVersion} />
          </>
        ) : null}
        <SourceLine info={sourceOf(block, provenance)} />
      </div>
    );
  }

  // no_exam_data, or a published status without a usable value: say so, never show a number.
  const version = block?.methodologyVersion ?? fallbackMethodologyVersion ?? null;
  return (
    <div className={classes(variant, true)} data-status={status}>
      <p className="score__value">Brak wyniku: nie ma danych egzaminacyjnych</p>
      {version ? <MethodologyLine version={version} /> : null}
      <SourceLine info={sourceOf(block, provenance)} />
    </div>
  );
}
