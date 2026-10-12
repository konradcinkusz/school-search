import type { ScoreBlock, ScoreStatus, TrendPoint } from "@/lib/api";
import { formatInteger, formatScore } from "@/lib/format";

/**
 * Inline SVG trend by exam year. Each published year is a dot with an interval whisker.
 * Suppressed years keep their x position and say "brak wyniku"; they are never drawn as zero.
 * The same numbers appear in the trend table on the profile page, so the chart is not the only source.
 */
interface Published {
  readonly examYear: number;
  readonly score: number;
  readonly low: number;
  readonly high: number;
}

function isPublishedBlock(status: ScoreStatus, block: ScoreBlock | null): block is ScoreBlock & {
  score: number;
  intervalLow: number;
  intervalHigh: number;
} {
  return (
    status === "published" &&
    block !== null &&
    block.score !== null &&
    block.intervalLow !== null &&
    block.intervalHigh !== null
  );
}

const WIDTH = 640;
const HEIGHT = 280;
const PAD_LEFT = 64;
const PAD_RIGHT = 24;
const PAD_TOP = 32;
const PAD_BOTTOM = 60;

export function TrendChart({ points }: { points: readonly TrendPoint[] }) {
  const years = [...points].sort((a, b) => a.examYear - b.examYear);
  if (years.length === 0) {
    return <p className="muted">Brak lat egzaminacyjnych do narysowania.</p>;
  }

  const published: Published[] = [];
  for (const point of years) {
    if (isPublishedBlock(point.scoreStatus, point.score) && point.score) {
      published.push({
        examYear: point.examYear,
        score: point.score.score,
        low: point.score.intervalLow,
        high: point.score.intervalHigh,
      });
    }
  }

  if (published.length === 0) {
    return (
      <p className="notice" role="note">
        Żaden rok nie ma publikowanego wyniku, więc wykres nie jest rysowany. Statusy każdego roku są w tabeli
        poniżej.
      </p>
    );
  }

  const plotWidth = WIDTH - PAD_LEFT - PAD_RIGHT;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const rawMin = Math.min(0, ...published.map((p) => p.low));
  const rawMax = Math.max(0, ...published.map((p) => p.high));
  const span = rawMax - rawMin || 1;
  const yMin = rawMin - span * 0.12;
  const yMax = rawMax + span * 0.12;

  // Points are inset from the plot edges so the year and "brak wyniku" labels never run outside the drawing.
  const inset = 44;
  const xFor = (index: number): number =>
    years.length === 1
      ? PAD_LEFT + plotWidth / 2
      : PAD_LEFT + inset + (index * (plotWidth - 2 * inset)) / (years.length - 1);
  const yFor = (value: number): number => PAD_TOP + ((yMax - value) / (yMax - yMin)) * plotHeight;

  const zeroY = yFor(0);
  // Axis labels that would overlap the zero label are dropped; the grid keeps the zero line.
  const ticks = [yMax, 0, yMin].filter((tick) => tick === 0 || Math.abs(yFor(tick) - zeroY) >= 22);
  // The top tick label is dropped when a value label sits on top of it; the grid line stays.
  const topTickY = yFor(yMax);
  const topTickClashes = published.some((p) => Math.abs(yFor(p.high) - 10 - topTickY) < 28);

  return (
    <figure className="trend">
      <div className="trend__scroll" role="region" aria-label="Wykres wyników (przewiń w bok na małym ekranie)" tabIndex={0}>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        width="100%"
        role="img"
        aria-labelledby="trend-chart-title trend-chart-desc"
        className="trend__svg"
      >
        <title id="trend-chart-title">Wynik szkoły w kolejnych latach egzaminu</title>
        <desc id="trend-chart-desc">
          Punkty pokazują wynik, a pionowe kreski przedział 95%. Lata bez publikowanego wyniku są podpisane jako
          brak wyniku.
        </desc>

        {ticks.map((tick) => (
          <g key={`tick-${tick}`}>
            <line className="trend__grid" x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={yFor(tick)} y2={yFor(tick)} />
            {tick === yMax && topTickClashes ? null : (
              <text className="trend__text trend__tick" x={PAD_LEFT - 10} y={yFor(tick) + 5} textAnchor="end">
                {formatScore(tick)}
              </text>
            )}
          </g>
        ))}

        <line className="trend__zero" x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={zeroY} y2={zeroY} />

        {years.map((point, index) => {
          const x = xFor(index);
          const label = (
            <text className="trend__text" x={x} y={HEIGHT - PAD_BOTTOM + 24} textAnchor="middle">
              {point.examYear}
            </text>
          );
          if (!isPublishedBlock(point.scoreStatus, point.score) || !point.score) {
            return (
              <g key={`year-${point.examYear}`}>
                {label}
                <text className="trend__note" x={x} y={HEIGHT - PAD_BOTTOM + 44} textAnchor="middle">
                  brak wyniku
                </text>
              </g>
            );
          }
          const block = point.score;
          const cx = x;
          return (
            <g key={`year-${point.examYear}`}>
              <line className="trend__whisker" x1={cx} x2={cx} y1={yFor(block.intervalHigh)} y2={yFor(block.intervalLow)} />
              <line className="trend__whisker" x1={cx - 8} x2={cx + 8} y1={yFor(block.intervalHigh)} y2={yFor(block.intervalHigh)} />
              <line className="trend__whisker" x1={cx - 8} x2={cx + 8} y1={yFor(block.intervalLow)} y2={yFor(block.intervalLow)} />
              <circle className="trend__dot" cx={cx} cy={yFor(block.score)} r={6} />
              <text className="trend__value" x={cx} y={yFor(block.intervalHigh) - 10} textAnchor="middle">
                {formatScore(block.score)}
              </text>
              {label}
              <text className="trend__note" x={cx} y={HEIGHT - PAD_BOTTOM + 44} textAnchor="middle">
                n={formatInteger(block.cohortSize)}
              </text>
            </g>
          );
        })}
      </svg>
      </div>
      <figcaption className="muted">
        Wykres pokazuje wynik i przedział 95% dla lat z publikowanym wynikiem. Pionowa kreska to przedział, a liczba
        pod rokiem to liczebność kohorty.
      </figcaption>
    </figure>
  );
}
