import type { FactSource, MoneyRange, OpeningHours } from "@/lib/api";
import { asOfLabel, formatInteger } from "@/lib/format";

/** Preschool presentation helpers. Values come from the API; these only format them, never judge them. */

const PLN = new Intl.NumberFormat("pl-PL", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

/** "1 250 zł" (amounts in whole złoty show no decimals). */
export function formatPln(value: number): string {
  return `${PLN.format(value)} zł`;
}

/** "od 600 zł do 900 zł miesięcznie", or null when both bounds are unknown. */
export function tuitionLabel(range: MoneyRange): string | null {
  const { minPln, maxPln } = range;
  if (minPln !== null && maxPln !== null) {
    return minPln === maxPln
      ? `${formatPln(minPln)} miesięcznie`
      : `od ${formatPln(minPln)} do ${formatPln(maxPln)} miesięcznie`;
  }
  if (minPln !== null) return `od ${formatPln(minPln)} miesięcznie`;
  if (maxPln !== null) return `do ${formatPln(maxPln)} miesięcznie`;
  return null;
}

/** "od 7:00 do 17:00", or null when neither time is known. */
export function hoursLabel(hours: OpeningHours): string | null {
  const { opens, closes } = hours;
  if (opens !== null && closes !== null) return `od ${opens} do ${closes}`;
  if (opens !== null) return `otwarte od ${opens}`;
  if (closes !== null) return `zamykane o ${closes}`;
  return null;
}

/** The count alone, for a value cell under its own label. Null when unknown. */
export function freePlacesValue(count: number | null): string | null {
  if (count === null) return null;
  if (count === 0) return "brak wolnych miejsc";
  return formatInteger(count);
}

/**
 * Badge text for a fact's confidence, as the API defines it: "reported" is a value copied from a primary public
 * source (no check by this portal is implied), "unverified" is a submitted value that has not been checked.
 * An unknown value is shown as unknown, never as verified.
 */
export function confidenceLabel(confidence: string): string {
  if (confidence === "reported") return "podane przez źródło";
  if (confidence === "unverified") return "niezweryfikowane";
  return "pewność nieznana";
}

/**
 * "Źródło: DEMO SEED (synthetic) · stan na 1 stycznia 2026". The source name is omitted when the API sends none;
 * the date reads "data nieznana" when it is missing, so a value never appears without its date.
 */
export function sourceLine(source: FactSource | null | undefined, asOf?: string | null): string {
  const name = source?.source?.trim() ?? "";
  const date = asOf !== undefined ? asOf : (source?.asOf ?? null);
  const parts: string[] = [];
  if (name !== "") parts.push(`Źródło: ${name}`);
  parts.push(asOfLabel(date));
  return parts.join(" · ");
}
