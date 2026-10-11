import type { GminaItem } from "@/lib/api";

/**
 * Address lookup as a plain GET form (no client JavaScript): the result is a shareable URL.
 * Gmina options come from /gminas; the label is the gmina name, or its TERYT code when the name is unknown.
 */
export interface AddressFormValues {
  readonly gmina?: string;
  readonly street?: string;
  readonly number?: string;
}

export function AddressForm({ gminas, values }: { gminas: readonly GminaItem[]; values: AddressFormValues }) {
  const sorted = [...gminas].sort((a, b) =>
    (a.name ?? a.teryt).localeCompare(b.name ?? b.teryt, "pl"),
  );
  return (
    <form method="get" action="/" className="address-form">
      <div className="field">
        <label htmlFor="gmina">Gmina</label>
        <select id="gmina" name="gmina" defaultValue={values.gmina ?? ""} required>
          <option value="">Wybierz gminę</option>
          {sorted.map((gmina) => (
            <option key={gmina.teryt} value={gmina.teryt}>
              {gmina.name ?? `TERYT ${gmina.teryt}`}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="ulica">Ulica</label>
        <input
          id="ulica"
          name="ulica"
          type="text"
          autoComplete="address-line1"
          maxLength={200}
          placeholder="np. Marszałkowska"
          defaultValue={values.street ?? ""}
          required
        />
      </div>
      <div className="field">
        <label htmlFor="numer">Numer domu (opcjonalnie)</label>
        <input
          id="numer"
          name="numer"
          type="text"
          inputMode="numeric"
          maxLength={20}
          autoComplete="address-line2"
          defaultValue={values.number ?? ""}
        />
      </div>
      <button type="submit" className="button">
        Sprawdź obwód i szkoły w zasięgu
      </button>
    </form>
  );
}
