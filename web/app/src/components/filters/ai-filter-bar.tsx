"use client";

import "@/app/styles/forms.css";
import "@/app/styles/filters.css";
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { AI_FILTER_LABEL, intentToSearchFilters, parseSearchIntent } from "@/lib/ai-contract";
import { aiFailureText } from "@/lib/ai-messages";
import { bffRequest, type BffResponse } from "@/lib/bff-client";
import {
  FILTER_LIMITS,
  describeFilters,
  removeFilter,
  sanitizeGmina,
  sanitizePlace,
  sanitizeRadius,
  sanitizeText,
  sanitizeTuition,
  type FacilityKind,
  type SearchFilters,
} from "@/lib/search-filters";
import { UNSUPPORTED_PREFIX } from "./helpers";

/**
 * Shared filter bar (Phase M): "Opisz czego szukasz" turns free text into editable filters, shown as removable chips.
 * The model never returns schools and never ranks. Manual editing works without AI; when AI is off only the AI part
 * degrades. The parent owns the filters (controlled); AI output replaces them, the visitor then edits or removes chips.
 */

const QUESTION_MAX = 300;

export interface AiFilterBarProps {
  filters: SearchFilters;
  onChange: (next: SearchFilters) => void;
  context: "map" | "list";
  /** Optional extra content rendered at the end of the panel (the list wrapper puts its results link here). */
  children?: ReactNode;
}

interface AiOutcome {
  readonly notes: readonly string[];
  readonly unsupported: readonly string[];
  readonly empty: boolean;
}

/** A text draft that follows the prop unless the visitor's half-typed text already means the same value. */
function useDraft(prop: string, same: (draft: string) => boolean): [string, (value: string) => void] {
  const [draft, setDraft] = useState(prop);
  useEffect(() => {
    setDraft((current) => (same(current) ? current : prop));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prop]);
  return [draft, setDraft];
}

export function AiFilterBar({ filters, onChange, context, children }: AiFilterBarProps) {
  const ids = {
    question: useId(),
    localError: useId(),
    heading: useId(),
    status: useId(),
    chips: useId(),
    edit: useId(),
    hint: useId(),
  };
  const [question, setQuestion] = useState("");
  const [sending, setSending] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<AiOutcome | null>(null);
  const [editOpen, setEditOpen] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const resultRef = useRef<HTMLDivElement>(null);
  const chipsRef = useRef<HTMLDivElement>(null);
  const questionRef = useRef<HTMLTextAreaElement>(null);

  // A new AI result is announced by moving focus to it (WCAG 4.1.3), as the explain panel does.
  useEffect(() => {
    if (outcome !== null) resultRef.current?.focus();
  }, [outcome]);

  const chips = describeFilters(filters);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const text = question.trim();
    if (text === "" || text.length > QUESTION_MAX) {
      setLocalError(text === "" ? "Napisz, czego szukasz." : `Opis może mieć najwyżej ${QUESTION_MAX} znaków.`);
      questionRef.current?.focus();
      return;
    }
    setLocalError(null);
    setFailure(null);
    setAnnouncement("");
    setSending(true);
    const response: BffResponse | null = await bffRequest("/api/proxy/api/v1/ai/search-intent", {
      method: "POST",
      body: { question: text },
    });
    setSending(false);
    const parsed = response?.status === 200 ? parseSearchIntent(response.body) : null;
    if (parsed === null) {
      setOutcome(null);
      setFailure(aiFailureText(response));
      return;
    }
    const next = intentToSearchFilters(parsed.filters);
    const count = describeFilters(next).length;
    onChange(next);
    setEditOpen(true);
    setAnnouncement(count === 0 ? "Nie ustawiono żadnych filtrów." : `Przygotowano filtry: ${count}.`);
    setOutcome({ notes: parsed.notes, unsupported: parsed.unsupported, empty: count === 0 });
  }

  function removeChip(key: keyof SearchFilters, label: string) {
    onChange(removeFilter(filters, key));
    setAnnouncement(`Usunięto filtr: ${label}.`);
    // The pressed button disappears: move focus to something that stays.
    if (chips.length > 1) chipsRef.current?.focus();
    else questionRef.current?.focus();
  }

  function clearAll() {
    onChange({ kind: "all" });
    setAnnouncement("Usunięto wszystkie filtry.");
    questionRef.current?.focus();
  }

  const showNote = outcome !== null && (outcome.unsupported.length > 0 || outcome.notes.length > 0);

  return (
    <section className="card ai-search ai-panel filter-bar" data-context={context} aria-labelledby={ids.heading}>
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
            ref={questionRef}
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
          {localError !== null ? (
            <p role="alert" id={ids.localError} className="field-error">
              {localError}
            </p>
          ) : null}
        </div>
        <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
          {sending ? "Przygotowywanie filtrów…" : "Przygotuj filtry"}
        </button>
      </form>

      <div id={ids.status} role="status" aria-live="polite">
        {sending ? <p className="muted">Przygotowywanie filtrów…</p> : null}
        {!sending && announcement !== "" ? <p className="visually-hidden">{announcement}</p> : null}
      </div>
      {failure !== null ? (
        <p role="alert" className="notice">
          {failure}
        </p>
      ) : null}

      {outcome !== null ? (
        <div ref={resultRef} tabIndex={-1} className="filter-bar__result focus-region">
          <p className="ai-box__label">{AI_FILTER_LABEL}</p>
          {outcome.empty ? (
            <p className="muted">Z tego opisu nie udało się ustawić żadnych filtrów. Możesz ustawić je ręcznie poniżej.</p>
          ) : null}
          {showNote ? (
            <div className="notice" role="note">
              {outcome.unsupported.length > 0 ? (
                <>
                  <p>{UNSUPPORTED_PREFIX.trim()}</p>
                  <ul>
                    {outcome.unsupported.map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ul>
                </>
              ) : null}
              {outcome.notes.length > 0 ? (
                <ul>
                  {outcome.notes.map((note) => (
                    <li key={note}>{note}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}

      {chips.length > 0 ? (
        <div ref={chipsRef} tabIndex={-1} className="filter-chips-wrap focus-region">
          <p id={ids.chips} className="filter-chips__title">
            Aktywne filtry
          </p>
          <ul className="filter-chips" aria-labelledby={ids.chips}>
            {chips.map((chip) => (
              <li key={chip.key} className="filter-chip">
                <span className="filter-chip__label">{chip.label}</span>
                <button
                  type="button"
                  className="filter-chip__remove"
                  aria-label={`Usuń filtr: ${chip.label}`}
                  onClick={() => removeChip(chip.key, chip.label)}
                >
                  <span aria-hidden="true">×</span>
                </button>
              </li>
            ))}
          </ul>
          {chips.length > 1 ? (
            <button type="button" className="link-button" onClick={clearAll}>
              Usuń wszystkie filtry
            </button>
          ) : null}
        </div>
      ) : null}

      <p className="actions">
        <button
          type="button"
          className="button button--secondary"
          aria-expanded={editOpen}
          aria-controls={ids.edit}
          onClick={() => setEditOpen((open) => !open)}
        >
          {editOpen ? "Ukryj ręczne ustawienia" : "Ustaw filtry ręcznie"}
        </button>
      </p>
      {editOpen ? <ManualFields id={ids.edit} hintId={ids.hint} filters={filters} onChange={onChange} context={context} /> : null}

      {children}
    </section>
  );
}

interface ManualProps {
  id: string;
  hintId: string;
  filters: SearchFilters;
  onChange: (next: SearchFilters) => void;
  context: "map" | "list";
}

type TextKey = "q" | "gmina" | "maxTuitionPln" | "place" | "radiusKm";

function ManualFields({ id, hintId, filters, onChange, context }: ManualProps) {
  const [q, setQ] = useDraft(filters.q ?? "", (d) => sanitizeText(d, FILTER_LIMITS.qMax) === filters.q);
  const [gmina, setGmina] = useDraft(filters.gmina ?? "", (d) => sanitizeGmina(d) === filters.gmina);
  const [tuition, setTuition] = useDraft(
    filters.maxTuitionPln !== undefined ? String(filters.maxTuitionPln) : "",
    (d) => sanitizeTuition(d) === filters.maxTuitionPln,
  );
  const [place, setPlace] = useDraft(filters.place ?? "", (d) => sanitizePlace(d) === filters.place);
  const [radius, setRadius] = useDraft(
    filters.radiusKm !== undefined ? String(filters.radiusKm) : "",
    (d) => sanitizeRadius(d) === filters.radiusKm,
  );

  const parsers: Record<TextKey, (raw: string) => string | number | undefined> = {
    q: (raw) => sanitizeText(raw, FILTER_LIMITS.qMax),
    gmina: (raw) => sanitizeGmina(raw),
    maxTuitionPln: (raw) => sanitizeTuition(raw),
    place: (raw) => sanitizePlace(raw),
    radiusKm: (raw) => sanitizeRadius(raw),
  };
  const messages: Record<TextKey, string> = {
    q: "Wpisz nazwę lub słowo kluczowe.",
    gmina: "Kod gminy ma siedem cyfr, bez spacji.",
    maxTuitionPln: `Podaj czesne w złotych, od ${FILTER_LIMITS.tuitionMin} do ${FILTER_LIMITS.tuitionMax}.`,
    place: "Wpisz nazwę miejscowości, ulicy lub placu, bez współrzędnych, adresu e-mail i numerów telefonu.",
    radiusKm: `Podaj odległość w km, od ${String(FILTER_LIMITS.radiusMin).replace(".", ",")} do ${FILTER_LIMITS.radiusMax}.`,
  };

  /** Empty input clears the filter, a valid one sets it, an invalid one changes nothing and shows the error. */
  function commit(key: TextKey, raw: string) {
    if (raw.trim() === "") {
      const next = { ...filters };
      delete next[key];
      onChange(next);
      return;
    }
    const value = parsers[key](raw);
    if (value === undefined) return;
    onChange({ ...filters, [key]: value });
  }
  const invalid = (key: TextKey, raw: string) => raw.trim() !== "" && parsers[key](raw) === undefined;

  const fieldProps = (key: TextKey, raw: string, setRaw: (v: string) => void, inputId: string, hint?: string) => {
    const bad = invalid(key, raw);
    return {
      id: inputId,
      value: raw,
      onChange: (event: { target: { value: string } }) => {
        setRaw(event.target.value);
        commit(key, event.target.value);
      },
      "aria-invalid": bad ? (true as const) : undefined,
      "aria-describedby": [hint, bad ? `${inputId}-error` : undefined].filter(Boolean).join(" ") || undefined,
    };
  };
  const error = (key: TextKey, raw: string, inputId: string) =>
    invalid(key, raw) ? (
      <p id={`${inputId}-error`} className="field-error">
        {messages[key]}
      </p>
    ) : null;

  const showPreschool = filters.kind !== "school";
  return (
    <fieldset id={id} className="filter-edit">
      <legend className="muted">Filtry (możesz je poprawić)</legend>
      <div className="field">
        <label htmlFor={`${id}-kind`}>Rodzaj placówki</label>
        <select
          id={`${id}-kind`}
          value={filters.kind}
          onChange={(event) => onChange({ ...filters, kind: event.target.value as FacilityKind })}
        >
          <option value="all">szkoły i przedszkola</option>
          <option value="school">szkoła podstawowa</option>
          <option value="preschool">przedszkole</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor={`${id}-q`}>Nazwa lub słowo kluczowe</label>
        <input type="text" maxLength={FILTER_LIMITS.qMax} {...fieldProps("q", q, setQ, `${id}-q`)} />
        {error("q", q, `${id}-q`)}
      </div>
      <div className="field">
        <label htmlFor={`${id}-gmina`}>Kod gminy (7 cyfr)</label>
        <input
          type="text"
          inputMode="numeric"
          maxLength={7}
          {...fieldProps("gmina", gmina, setGmina, `${id}-gmina`, `${hintId}-gmina`)}
        />
        <p id={`${hintId}-gmina`} className="field-hint">
          Siedem cyfr, bez spacji.
        </p>
        {error("gmina", gmina, `${id}-gmina`)}
      </div>
      <div className="field field--check">
        <label className="check-label">
          <input
            type="checkbox"
            checked={filters.publicOnly === true}
            onChange={(event) => {
              const next = { ...filters };
              if (event.target.checked) next.publicOnly = true;
              else delete next.publicOnly;
              onChange(next);
            }}
          />{" "}
          Tylko publiczne
        </label>
      </div>
      {showPreschool ? (
        <>
          <div className="field">
            <label htmlFor={`${id}-tuition`}>Czesne do (zł miesięcznie)</label>
            <input
              type="text"
              inputMode="numeric"
              maxLength={6}
              placeholder="np. 600"
              {...fieldProps("maxTuitionPln", tuition, setTuition, `${id}-tuition`)}
            />
            {error("maxTuitionPln", tuition, `${id}-tuition`)}
          </div>
          <div className="field field--check">
            <label className="check-label">
              <input
                type="checkbox"
                checked={filters.hasFreePlaces === true}
                onChange={(event) => {
                  const next = { ...filters };
                  if (event.target.checked) next.hasFreePlaces = true;
                  else delete next.hasFreePlaces;
                  onChange(next);
                }}
              />{" "}
              Są wolne miejsca
            </label>
          </div>
        </>
      ) : null}
      <div className="field">
        <label htmlFor={`${id}-place`}>Miejsce (np. Plac Wolności w Poznaniu)</label>
        <input
          type="text"
          maxLength={FILTER_LIMITS.placeMax}
          {...fieldProps("place", place, setPlace, `${id}-place`, `${hintId}-place`)}
        />
        <p id={`${hintId}-place`} className="field-hint">
          {context === "map"
            ? "Mapa przesunie się w to miejsce. Miejsce nie ustala obwodu szkolnego."
            : "Miejsce i odległość zastosujemy na mapie. Nie ustalają obwodu szkolnego."}
        </p>
        {error("place", place, `${id}-place`)}
      </div>
      <div className="field">
        <label htmlFor={`${id}-radius`}>Najwyżej km od miejsca</label>
        <input
          type="text"
          inputMode="decimal"
          maxLength={5}
          placeholder="np. 3"
          {...fieldProps("radiusKm", radius, setRadius, `${id}-radius`)}
        />
        {error("radiusKm", radius, `${id}-radius`)}
      </div>
    </fieldset>
  );
}
