"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import type { GminaItem } from "@/lib/api";
import { HOUSE_NUMBER_MESSAGE, HOUSE_NUMBER_PATTERN } from "@/lib/house-number";

/**
 * Address lookup as a GET form: the result is a shareable URL. Gmina options come from /gminas; the label is the
 * gmina name, or "Nieznana gmina" with its code when the name is unknown.
 *
 * Native validation is off (noValidate) so the same checks run in one place: on submit, an invalid form is stopped,
 * each bad field gets aria-invalid and an inline .field-error, and focus moves to the first one. A valid form is not
 * prevented, so it submits natively to the same URL.
 */

type AddressField = "gmina" | "street" | "number";
type AddressErrors = Partial<Record<AddressField, string>>;

/** Field order is the order focus moves in when several fields are invalid. */
const FIELD_ORDER: readonly AddressField[] = ["gmina", "street", "number"];
const FIELD_ID: Record<AddressField, string> = { gmina: "gmina", street: "ulica", number: "numer" };

export interface AddressFormValues {
  readonly gmina?: string;
  readonly street?: string;
  readonly number?: string;
}

export function AddressForm({ gminas, values }: { gminas: readonly GminaItem[]; values: AddressFormValues }) {
  const sorted = [...gminas].sort((a, b) =>
    (a.name ?? a.teryt).localeCompare(b.name ?? b.teryt, "pl"),
  );
  const ids = {
    gminaError: useId(),
    streetError: useId(),
    numberError: useId(),
  };
  const errorId: Record<AddressField, string> = {
    gmina: ids.gminaError,
    street: ids.streetError,
    number: ids.numberError,
  };
  const [errors, setErrors] = useState<AddressErrors>({});

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    const data = new FormData(event.currentTarget);
    const gmina = String(data.get("gmina") ?? "");
    const street = String(data.get("ulica") ?? "").trim();
    const number = String(data.get("numer") ?? "").trim();

    const next: AddressErrors = {};
    if (gmina === "") next.gmina = "Wybierz gminę.";
    if (street === "") next.street = "Podaj nazwę ulicy.";
    if (number !== "" && !HOUSE_NUMBER_PATTERN.test(number)) next.number = HOUSE_NUMBER_MESSAGE;

    setErrors(next);
    const first = FIELD_ORDER.find((field) => next[field] !== undefined);
    if (first === undefined) return; // Valid: let the GET submission go through to the same URL.
    event.preventDefault();
    document.getElementById(FIELD_ID[first])?.focus();
  }

  /** Clears one field's error as soon as the visitor changes it. */
  function clear(field: AddressField) {
    setErrors((current) => (current[field] === undefined ? current : { ...current, [field]: undefined }));
  }

  return (
    <form method="get" action="/" className="address-form form-grid form-grid--address" onSubmit={onSubmit} noValidate>
      <div className="field form-grid__full">
        <label htmlFor="gmina">Gmina</label>
        <select
          id="gmina"
          name="gmina"
          defaultValue={values.gmina ?? ""}
          required
          onChange={() => clear("gmina")}
          aria-invalid={errors.gmina ? true : undefined}
          aria-describedby={errors.gmina ? errorId.gmina : undefined}
        >
          <option value="">Wybierz gminę</option>
          {sorted.map((gmina) => (
            <option key={gmina.teryt} value={gmina.teryt}>
              {gmina.name ?? `Nieznana gmina (kod ${gmina.teryt})`}
            </option>
          ))}
        </select>
        {errors.gmina ? (
          <p id={ids.gminaError} className="field-error">
            {errors.gmina}
          </p>
        ) : null}
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
          onChange={() => clear("street")}
          aria-invalid={errors.street ? true : undefined}
          aria-describedby={errors.street ? errorId.street : undefined}
        />
        {errors.street ? (
          <p id={ids.streetError} className="field-error">
            {errors.street}
          </p>
        ) : null}
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
          onChange={() => clear("number")}
          aria-invalid={errors.number ? true : undefined}
          aria-describedby={errors.number ? errorId.number : undefined}
        />
        {errors.number ? (
          <p id={ids.numberError} className="field-error">
            {errors.number}
          </p>
        ) : null}
      </div>
      <div className="form-grid__actions">
        <button type="submit" className="button button--block" style={{ textWrap: "balance" }}>
          Sprawdź obwód i szkoły w zasięgu
        </button>
      </div>
    </form>
  );
}
