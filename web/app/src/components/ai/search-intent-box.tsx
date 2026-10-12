"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useId, useState, type FormEvent } from "react";
import { AI_LABEL, parseSearchIntent, type SchoolKind, type SearchIntentFilters, type SearchIntentResult } from "@/lib/ai-contract";
import { aiFailureText } from "@/lib/ai-messages";
import { bffRequest, type BffResponse } from "@/lib/bff-client";

/**
 * "Opisz czego szukasz": free text becomes editable filter chips. The model never returns schools and never ranks:
 * the chips only prepare an ordinary search. Nothing navigates by itself; the visitor follows the link.
 */

const QUESTION_MAX = 300;
const MAX_DISTANCE_KM = 50;

/** Filter fields as the visitor edits them. Strings keep half-typed input intact. */
interface Editable {
  readonly gmina: string;
  readonly q: string;
  readonly publicOnly: boolean;
  readonly maxDistanceKm: string;
  readonly type: "" | SchoolKind;
}

function toEditable(filters: SearchIntentFilters): Editable {
  return {
    gmina: filters.gmina ?? "",
    q: filters.q ?? "",
    publicOnly: filters.publicOnly === true,
    maxDistanceKm: filters.maxDistanceKm !== undefined ? String(filters.maxDistanceKm) : "",
    type: filters.type ?? "",
  };
}

/** Results page for the filters that the portal can apply today. Distance and public-only are reported, not faked. */
export function resultsHref(draft: Editable): { href: string; unapplied: string[] } {
  const params = new URLSearchParams();
  const gmina = /^[0-9]{7}$/.test(draft.gmina.trim()) ? draft.gmina.trim() : "";
  const q = draft.q.trim().slice(0, 100);
  if (gmina) params.set("gmina", gmina);
  if (q) params.set("q", q);
  const unapplied: string[] = [];
  const distance = Number(draft.maxDistanceKm);
  if (draft.maxDistanceKm !== "" && Number.isFinite(distance) && distance > 0) {
    unapplied.push("odległość od adresu (podaj adres w formularzu obwodu)");
  }
  if (draft.type === "preschool") {
    if (draft.publicOnly) params.set("publicOnly", "true");
    const query = params.toString();
    return { href: query ? `/przedszkola?${query}` : "/przedszkola", unapplied };
  }
  if (draft.publicOnly) unapplied.push("tylko placówki publiczne (wyniki szkół)");
  const query = params.toString();
  return { href: query ? `/?${query}` : "/", unapplied };
}

export function SearchIntentBox() {
  const ids = {
    question: useId(),
    localError: useId(),
    heading: useId(),
    status: useId(),
    filters: useId(),
    gminaHint: useId(),
  };
  const [question, setQuestion] = useState("");
  const [sending, setSending] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [result, setResult] = useState<SearchIntentResult | null>(null);
  const [draft, setDraft] = useState<Editable | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const text = question.trim();
    if (text === "" || text.length > QUESTION_MAX) {
      setLocalError(text === "" ? "Napisz, czego szukasz." : `Opis może mieć najwyżej ${QUESTION_MAX} znaków.`);
      document.getElementById(ids.question)?.focus();
      return;
    }
    setLocalError(null);
    setFailure(null);
    setSending(true);
    const response: BffResponse | null = await bffRequest("/api/proxy/api/v1/ai/search-intent", {
      method: "POST",
      body: { question: text },
    });
    setSending(false);
    const parsed = response?.status === 200 ? parseSearchIntent(response.body) : null;
    if (parsed === null) {
      setResult(null);
      setDraft(null);
      setFailure(aiFailureText(response));
      return;
    }
    setResult(parsed);
    setDraft(toEditable(parsed.filters));
  }

  function patch(change: Partial<Editable>) {
    setDraft((current) => (current === null ? current : { ...current, ...change }));
  }

  const link = draft !== null ? resultsHref(draft) : null;

  return (
    <section className="card ai-search ai-panel" aria-labelledby={ids.heading}>
      <div className="ai-panel__head">
        <h2 id={ids.heading}>Opisz czego szukasz</h2>
        <span className="badge badge--ai" aria-hidden="true">
          AI
        </span>
      </div>
      <p className="muted">
        Napisz zwykłymi słowami, na przykład „szkoła podstawowa w gminie Przykładowo, z oddziałem sportowym”. AI tylko
        przygotuje filtry, które możesz poprawić. Portal nie ocenia szkół ani nie układa ich według jakości.
      </p>
      <form onSubmit={onSubmit} noValidate>
        <p className="field-hint">Pola oznaczone * są wymagane.</p>
        <div className="field">
          <label htmlFor={ids.question}>
            Opisz czego szukasz<span aria-hidden="true"> *</span>
          </label>
          <textarea
            id={ids.question}
            name="question"
            rows={3}
            maxLength={QUESTION_MAX}
            value={question}
            required
            onChange={(event) => setQuestion(event.target.value)}
            aria-invalid={localError !== null ? true : undefined}
            aria-describedby={localError !== null ? `${ids.status} ${ids.localError}` : ids.status}
          />
        </div>
        <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
          {sending ? "Przygotowywanie filtrów…" : "Przygotuj filtry"}
        </button>
      </form>

      <div id={ids.status} role="status" aria-live="polite">
        {sending ? <p className="muted">Przygotowywanie filtrów…</p> : null}
      </div>
      {localError !== null ? (
        <p role="alert" id={ids.localError} className="notice">
          {localError}
        </p>
      ) : null}
      {failure !== null ? (
        <p role="alert" className="notice">
          {failure}
        </p>
      ) : null}

      {result !== null && draft !== null && link !== null ? (
        <div className="ai-search__result">
          <p className="ai-box__label">{result.aiLabel || AI_LABEL}</p>
          <fieldset className="chips" id={ids.filters}>
            <legend className="muted">Zinterpretowane filtry (możesz je poprawić)</legend>
            <div className="chip">
              <label htmlFor={`${ids.filters}-gmina`}>Kod gminy (7 cyfr)</label>
              <input
                id={`${ids.filters}-gmina`}
                type="text"
                inputMode="numeric"
                maxLength={7}
                value={draft.gmina}
                aria-describedby={ids.gminaHint}
                onChange={(event) => patch({ gmina: event.target.value })}
              />
              <p id={ids.gminaHint} className="field-hint">
                Siedem cyfr, bez spacji.
              </p>
            </div>
            <div className="chip">
              <label htmlFor={`${ids.filters}-q`}>Nazwa lub słowo kluczowe</label>
              <input
                id={`${ids.filters}-q`}
                type="text"
                maxLength={100}
                value={draft.q}
                onChange={(event) => patch({ q: event.target.value })}
              />
            </div>
            <div className="chip">
              <label htmlFor={`${ids.filters}-type`}>Rodzaj placówki</label>
              <select
                id={`${ids.filters}-type`}
                value={draft.type}
                onChange={(event) => patch({ type: event.target.value as Editable["type"] })}
              >
                <option value="">dowolna</option>
                <option value="school">szkoła podstawowa</option>
                <option value="preschool">przedszkole</option>
              </select>
            </div>
            <div className="chip chip--check">
              <input
                id={`${ids.filters}-public`}
                type="checkbox"
                checked={draft.publicOnly}
                onChange={(event) => patch({ publicOnly: event.target.checked })}
              />
              <label htmlFor={`${ids.filters}-public`}>Tylko publiczne</label>
            </div>
            <div className="chip">
              <label htmlFor={`${ids.filters}-distance`}>Najwyżej km od adresu</label>
              <input
                id={`${ids.filters}-distance`}
                type="number"
                min={0.5}
                max={MAX_DISTANCE_KM}
                step={0.5}
                value={draft.maxDistanceKm}
                onChange={(event) => patch({ maxDistanceKm: event.target.value })}
              />
            </div>
          </fieldset>

          {result.unsupported.length > 0 ? (
            <div className="notice" role="note">
              <p>Tej części pytania portal nie obsługuje, dlatego nie została użyta jako filtr:</p>
              <ul>
                {result.unsupported.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
              <p>Portal nie ustala kolejności szkół według jakości. Szkoły możesz porównać na stronie porównania.</p>
            </div>
          ) : null}
          {result.notes.length > 0 ? (
            <ul className="muted">
              {result.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          ) : null}

          <p className="actions">
            <Link href={link.href} className="button">
              Pokaż wyniki z tymi filtrami
            </Link>
          </p>
          {link.unapplied.length > 0 ? (
            <p className="muted">Wyników nie zawężamy według: {link.unapplied.join("; ")}.</p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
