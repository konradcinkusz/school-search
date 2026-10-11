"use client";

import { useId, useState, type FormEvent } from "react";

/**
 * Placeholder wedge flow: address -> catchment school -> schools in reach.
 * No geocoding or lookup happens yet; the form only echoes the address back.
 */
export function AddressForm() {
  const inputId = useId();
  const [address, setAddress] = useState("");
  const [submitted, setSubmitted] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = address.trim();
    if (trimmed === "") {
      setSubmitted(null);
      setError("Wpisz adres, aby kontynuować.");
      return;
    }
    setError(null);
    setSubmitted(trimmed);
  }

  return (
    <div>
      <form onSubmit={onSubmit} noValidate aria-describedby={error ? `${inputId}-error` : undefined}>
        <div className="field">
          <label htmlFor={inputId}>Adres zamieszkania</label>
          <input
            id={inputId}
            name="address"
            type="text"
            autoComplete="street-address"
            placeholder="np. ulica, numer domu, miejscowość"
            value={address}
            onChange={(event) => setAddress(event.target.value)}
          />
        </div>
        {error ? (
          <p id={`${inputId}-error`} role="alert">
            {error}
          </p>
        ) : null}
        <button type="submit" className="button">
          Sprawdź szkoły w zasięgu
        </button>
      </form>

      {submitted ? (
        <section className="card" aria-live="polite">
          <h2>Obwód szkolny</h2>
          <p>
            Adres: <strong>{submitted}</strong>
          </p>
          <ol>
            <li>Ustalenie obwodu szkolnego: w przygotowaniu.</li>
            <li>Wyniki szkoły z obwodu: w przygotowaniu.</li>
            <li>Szkoły w zasięgu: w przygotowaniu.</li>
          </ol>
          <p className="muted">Dla tego adresu nie prezentujemy jeszcze żadnego wyniku.</p>
        </section>
      ) : null}
    </div>
  );
}
