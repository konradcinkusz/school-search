import type { GminaItem } from "@/lib/api";
import { filtersFromSearchParams, type SearchFilters } from "@/lib/search-filters";

/**
 * Preschool search as a plain GET form (no client JavaScript): the result is a shareable URL.
 * Gmina options come from /gminas, the same picker as the home page.
 */
export interface PreschoolFilterValues {
  readonly gmina?: string;
  readonly q?: string;
  readonly maxTuition?: string;
  readonly publicOnly: boolean;
  readonly hasFreePlaces: boolean;
}

/** Form values from the shared model. The GET form's field names equal the shared URL parameters. */
export function preschoolValuesFromFilters(filters: SearchFilters): PreschoolFilterValues {
  return {
    gmina: filters.gmina,
    q: filters.q,
    maxTuition: filters.maxTuitionPln !== undefined ? String(filters.maxTuitionPln) : undefined,
    publicOnly: filters.publicOnly === true,
    hasFreePlaces: filters.hasFreePlaces === true,
  };
}

/** The shared model for the values of this form (kind is always preschool). Invalid input is dropped. */
export function preschoolValuesToFilters(values: PreschoolFilterValues): SearchFilters {
  const params = new URLSearchParams();
  if (values.gmina) params.set("gmina", values.gmina);
  if (values.q) params.set("q", values.q);
  if (values.maxTuition) params.set("maxTuition", values.maxTuition);
  if (values.publicOnly) params.set("publicOnly", "true");
  if (values.hasFreePlaces) params.set("hasFreePlaces", "true");
  return { ...filtersFromSearchParams(params), kind: "preschool" };
}

export function PreschoolFilters({
  gminas,
  values: valuesProp,
  filters,
}: {
  gminas: readonly GminaItem[];
  values: PreschoolFilterValues;
  /** Optional shared-model input; when given it replaces `values`. */
  filters?: SearchFilters;
}) {
  const values = filters !== undefined ? preschoolValuesFromFilters(filters) : valuesProp;
  const sorted = [...gminas].sort((a, b) => (a.name ?? a.teryt).localeCompare(b.name ?? b.teryt, "pl"));
  return (
    <form method="get" action="/przedszkola" className="form-grid form-grid--filters">
      <div className="field">
        <label htmlFor="pre-gmina">Gmina</label>
        <select id="pre-gmina" name="gmina" defaultValue={values.gmina ?? ""}>
          <option value="">Wszystkie gminy</option>
          {sorted.map((gmina) => (
            <option key={gmina.teryt} value={gmina.teryt}>
              {gmina.name ?? `TERYT ${gmina.teryt}`}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="pre-q">Nazwa przedszkola</label>
        <input
          id="pre-q"
          name="q"
          type="text"
          maxLength={100}
          autoComplete="off"
          defaultValue={values.q ?? ""}
        />
      </div>
      <div className="field">
        <label htmlFor="pre-max">Maksymalne czesne (zł miesięcznie, opcjonalnie)</label>
        <input
          id="pre-max"
          name="maxTuition"
          type="text"
          inputMode="decimal"
          maxLength={12}
          placeholder="np. 600"
          defaultValue={values.maxTuition ?? ""}
        />
      </div>
      <div className="form-grid__full filters__checks">
        <label className="check-label">
          <input type="checkbox" name="publicOnly" value="true" defaultChecked={values.publicOnly} /> Tylko publiczne
        </label>
        <label className="check-label">
          <input type="checkbox" name="hasFreePlaces" value="true" defaultChecked={values.hasFreePlaces} /> Są wolne
          miejsca
        </label>
      </div>
      <div className="form-grid__actions">
        <button type="submit" className="button">
          Szukaj przedszkoli
        </button>
        <a href="/przedszkola" className="link-button">
          Wyczyść filtry
        </a>
      </div>
    </form>
  );
}
