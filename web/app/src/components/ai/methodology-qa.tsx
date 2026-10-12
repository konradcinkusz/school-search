"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import { AI_LABEL, parseMethodologyAnswer, type MethodologyAnswer } from "@/lib/ai-contract";
import { aiFailureText } from "@/lib/ai-messages";
import { bffRequest, type BffResponse } from "@/lib/bff-client";

/**
 * "Zapytaj o metodologię": answers come only from the methodology document, with the sections it cites.
 * A question the document does not cover gets the fixed "Tego nie opisuje metodologia" answer from the service.
 */

const QUESTION_MAX = 300;

export function MethodologyQa() {
  const ids = { question: useId(), localError: useId(), heading: useId() };
  const [question, setQuestion] = useState("");
  const [sending, setSending] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [answer, setAnswer] = useState<MethodologyAnswer | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const text = question.trim();
    if (text === "" || text.length > QUESTION_MAX) {
      setLocalError(text === "" ? "Wpisz pytanie o metodologię." : `Pytanie może mieć najwyżej ${QUESTION_MAX} znaków.`);
      document.getElementById(ids.question)?.focus();
      return;
    }
    setLocalError(null);
    setFailure(null);
    setSending(true);
    const response: BffResponse | null = await bffRequest("/api/proxy/api/v1/ai/methodology-answer", {
      method: "POST",
      body: { question: text },
    });
    setSending(false);
    const parsed = response?.status === 200 ? parseMethodologyAnswer(response.body) : null;
    if (parsed === null) {
      setAnswer(null);
      setFailure(aiFailureText(response));
      return;
    }
    setAnswer(parsed);
  }

  return (
    <section className="card ai-explain ai-panel" aria-labelledby={ids.heading}>
      <div className="ai-panel__head">
        <h2 id={ids.heading}>Zapytaj o metodologię</h2>
        <span className="badge badge--ai" aria-hidden="true">
          AI
        </span>
      </div>
      <p className="muted">
        Asystent odpowiada wyłącznie na podstawie tego dokumentu. Nie odpowiada o konkretne szkoły ani o to, która szkoła
        jest lepsza.
      </p>
      <form onSubmit={onSubmit} noValidate>
        <p className="field-hint">Pola oznaczone * są wymagane.</p>
        <div className="field">
          <label htmlFor={ids.question}>
            Twoje pytanie o metodologię<span aria-hidden="true"> *</span>
          </label>
          <textarea
            id={ids.question}
            rows={2}
            maxLength={QUESTION_MAX}
            value={question}
            required
            aria-invalid={localError !== null ? true : undefined}
            aria-describedby={localError !== null ? ids.localError : undefined}
            onChange={(e) => setQuestion(e.target.value)}
          />
        </div>
        <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
          {sending ? "Szukanie w metodologii…" : "Zapytaj"}
        </button>
      </form>

      <div role="status" aria-live="polite">
        {sending ? <p className="muted">Szukanie w metodologii…</p> : null}
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

      {answer !== null ? (
        <section className="ai-box" aria-label="Odpowiedź AI">
          <p className="ai-box__label">{answer.aiLabel || AI_LABEL}</p>
          <p className="ai-box__text">{answer.answer}</p>
          {answer.citedSections.length > 0 ? (
            <div>
              <p className="muted">Odpowiedź opiera się na sekcjach:</p>
              <ul>
                {answer.citedSections.map((section) => (
                  <li key={section}>{section}</li>
                ))}
              </ul>
            </div>
          ) : null}
          <p className="muted ai-box__meta">
            Pełny tekst zasad opisuje ta strona. Odpowiedź AI nie zastępuje metodologii.
          </p>
        </section>
      ) : null}
    </section>
  );
}
