/** Polish number and date formatting. Values come from the API; formatting never changes them. */

const SCORE_FORMAT = new Intl.NumberFormat("pl-PL", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
  signDisplay: "exceptZero",
});

const ONE_DECIMAL = new Intl.NumberFormat("pl-PL", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const INTEGER = new Intl.NumberFormat("pl-PL");

const DATE_FORMAT = new Intl.DateTimeFormat("pl-PL", {
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});

/** Signed score with one decimal, for example "+1,2" or "−0,4". */
export function formatScore(value: number): string {
  return SCORE_FORMAT.format(value);
}

/** Unsigned value with one decimal, for example "0,8" (axis labels, half widths). */
export function formatDecimal(value: number): string {
  return ONE_DECIMAL.format(value);
}

export function formatInteger(value: number): string {
  return INTEGER.format(value);
}

/** Percent from a 0..100 value, for example "87 %" with a non-breaking space. */
export function formatPercent(value: number): string {
  return `${INTEGER.format(Math.round(value))} %`;
}

/** "5 uczniów" with the singular for one. */
export function formatStudents(count: number): string {
  return count === 1 ? "1 uczeń" : `${INTEGER.format(count)} uczniów`;
}

/** Formats an ISO date (yyyy-mm-dd, optionally followed by a time). Returns null when it is not a date. */
export function formatDatePl(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!match) return null;
  const date = new Date(`${match[1]}-${match[2]}-${match[3]}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) return null;
  return DATE_FORMAT.format(date);
}

/** "stan na 1 października 2026", or a plain statement when the date is unknown. */
export function asOfLabel(iso: string | null | undefined): string {
  const formatted = formatDatePl(iso);
  return formatted ? `stan na ${formatted}` : "data nieznana";
}

/** School year 2025 means the 2025/2026 school year. */
export function schoolYearLabel(startYear: number): string {
  return `${startYear}/${startYear + 1}`;
}

export function formatDistanceKm(value: number): string {
  return `${ONE_DECIMAL.format(value)} km`;
}
