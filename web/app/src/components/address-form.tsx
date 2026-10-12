import type { GminaItem } from "@/lib/api";

/**
 * Address lookup as a plain GET form (no client JavaScript): the result is a shareable URL.
 * Gmina options come from /gminas; the label is the gmina name, or "Nieznana gmina" with its code when the name is unknown.
 */
/**
 * House number: digits, an optional letter, and an optional second part after a slash (5, 5A, 12/4B).
 * Shared by the form (HTML pattern, which the browser checks) and the page (server check, which always runs).
 */
export const HOUSE_NUMBER_SOURCE = "\\d{1,4}[A-Za-z]?(?:/\\d{1,4}[A-Za-z]?)?";
export const HOUSE_NUMBER_PATTERN = new RegExp(`^${HOUSE_NUMBER_SOURCE}$`);
export const HOUSE_NUMBER_MESSAGE = "Numer domu podaj cyframi, na przykład 5 lub 5A.";

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
    <form method="get" action="/" className="address-form form-grid form-grid--address">
      <div className="field form-grid__full">
        <label htmlFor="gmina">Gmina</label>
        <select id="gmina" name="gmina" defaultValue={values.gmina ?? ""} required>
          <option value="">Wybierz gminę</option>
          {sorted.map((gmina) => (
            <option key={gmina.teryt} value={gmina.teryt}>
              {gmina.name ?? `Nieznana gmina (kod ${gmina.teryt})`}
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
          pattern={HOUSE_NUMBER_SOURCE}
          title={HOUSE_NUMBER_MESSAGE}
          maxLength={20}
          autoComplete="address-line2"
          defaultValue={values.number ?? ""}
        />
      </div>
      <div className="form-grid__actions">
        <button type="submit" className="button button--block" style={{ textWrap: "balance" }}>
          Sprawdź obwód i szkoły w zasięgu
        </button>
      </div>
    </form>
  );
}
