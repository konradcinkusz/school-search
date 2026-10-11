import type { MoneyRange, OpeningHours } from "@/lib/api";
import { formatInteger } from "@/lib/format";

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

/** Null when the count is unknown. Zero is a real answer: no vacancies were reported. */
export function freePlacesLabel(count: number | null): string | null {
  if (count === null) return null;
  if (count === 0) return "brak wolnych miejsc";
  return `wolne miejsca: ${formatInteger(count)}`;
}

/** Badge text for a fact's confidence. An unknown value is shown as unknown, never as verified. */
export function confidenceLabel(confidence: string): string {
  if (confidence === "reported") return "zweryfikowane źródło";
  if (confidence === "unverified") return "niezweryfikowane";
  return "pewność nieznana";
}
