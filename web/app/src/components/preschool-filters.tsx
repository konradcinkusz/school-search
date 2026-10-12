import type { GminaItem } from "@/lib/api";

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

export function PreschoolFilters({ gminas, values }: { gminas: readonly GminaItem[]; values: PreschoolFilterValues }) {
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
      </div>
    </form>
  );
}
