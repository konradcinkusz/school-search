import type { CoverageState } from "@/lib/api";
import { asOfLabel, formatDatePl } from "@/lib/format";
import { safeHttpUrl } from "@/lib/validation";

const LABEL: Record<CoverageState, string> = {
  polygon: "Obwody: granice (poligony)",
  street_list: "Obwody: lista ulic",
  none: "Brak danych o obwodach",
};

export interface CoverageBadgeProps {
  readonly state: CoverageState;
  readonly resolutionUrl: string | null;
  readonly resolutionDate: string | null;
}

export function CoverageBadge({ state, resolutionUrl, resolutionDate }: CoverageBadgeProps) {
  const safeUrl = safeHttpUrl(resolutionUrl);
  const dateLabel = formatDatePl(resolutionDate) ? asOfLabel(resolutionDate) : "data uchwały nieznana";
  return (
    <div className="coverage" data-state={state}>
      <p>
        <span className="badge">{LABEL[state]}</span>{" "}
        <span className="muted">{dateLabel}</span>
      </p>
      {safeUrl ? (
        <p>
          <a href={safeUrl} rel="noopener noreferrer nofollow">
            Uchwała gminy o obwodach (źródło)
          </a>
        </p>
      ) : (
        <p className="muted">Link do uchwały gminy nie jest znany.</p>
      )}
    </div>
  );
}
