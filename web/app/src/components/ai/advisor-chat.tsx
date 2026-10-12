"use client";

import "@/app/styles/forms.css";
import Link from "next/link";
import { useId, useState, type FormEvent } from "react";
import { AI_LABEL, isRecord, parseAgentReply, type AgentReply } from "@/lib/ai-contract";
import { aiFailureText, stopReasonText } from "@/lib/ai-messages";
import { isSlug } from "@/lib/validation";

/**
 * "Doradca wyboru szkoły": a short chat with the advisor agent (BFF /api/agent). Every assistant message is labelled
 * as AI output, shows the lookups it made and links the schools it cites. The conversation lives in this tab only.
 */

const TURN_MAX = 1000;
const MAX_TURNS = 12;

interface Turn {
  readonly role: "user" | "assistant";
  readonly content: string;
  /** Present on assistant turns that came from a successful answer. */
  readonly answer?: AgentReply;
}

export function AdvisorChat() {
  const ids = { input: useId(), heading: useId(), error: useId(), limit: useId() };
  const [turns, setTurns] = useState<Turn[]>([]);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** True only when the message box itself is wrong (empty or too long); the box then points at the alert. */
  const [draftInvalid, setDraftInvalid] = useState(false);

  const full = turns.length >= MAX_TURNS;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending || full) return;
    const text = draft.trim();
    if (text === "" || text.length > TURN_MAX) {
      setError(text === "" ? "Napisz, czego szukasz, zanim wyślesz wiadomość." : `Wiadomość może mieć najwyżej ${TURN_MAX} znaków.`);
      setDraftInvalid(true);
      document.getElementById(ids.input)?.focus();
      return;
    }

    // The visitor's message joins the transcript only when the agent answers, so a retry never sends two user turns.
    const next: Turn[] = [...turns, { role: "user", content: text }];
    setDraft("");
    setError(null);
    setDraftInvalid(false);
    setSending(true);

    const messages = next.map((turn) => ({ role: turn.role, content: turn.content }));
    let outcome: { ok: true; answer: AgentReply } | { ok: false; text: string };
    try {
      const response = await fetch("/api/agent", {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        cache: "no-store",
        body: JSON.stringify({ messages }),
      });
      const payload: unknown = await response.json().catch(() => null);
      const parsed = response.status === 200 ? parseAgentReply(payload) : null;
      outcome =
        parsed !== null
          ? { ok: true, answer: parsed }
          : {
              ok: false,
              text: aiFailureText({
                status: response.status,
                body: isRecord(payload) ? payload : null,
              }),
            };
    } catch {
      outcome = { ok: false, text: aiFailureText(null) };
    }
    setSending(false);

    if (outcome.ok) {
      setTurns([...next, { role: "assistant", content: outcome.answer.reply, answer: outcome.answer }]);
    } else {
      // Keep the text in the box so the visitor can send it again without retyping.
      setDraft(text);
      setError(outcome.text);
    }
  }

  function startOver() {
    setTurns([]);
    setDraft("");
    setError(null);
  }

  return (
    <section className="ai-chat" aria-labelledby={ids.heading}>
      <h2 id={ids.heading}>Rozmowa</h2>

      {turns.length === 0 ? (
        <p className="muted">
          Opisz swoją sytuację, na przykład: „Mieszkamy przy ulicy Długiej, szukam szkoły z dobrą opieką po lekcjach”.
          Doradca sprawdzi dane w portalu i powie, co z nich wynika.
        </p>
      ) : null}

      <ol className="ai-chat__log" aria-live="polite">
        {turns.map((turn, index) => (
          <li key={`${index}-${turn.role}`} className={turn.role === "user" ? "ai-msg ai-msg--user" : "ai-msg ai-msg--assistant"}>
            {turn.role === "user" ? (
              <p>
                <span className="muted">Ty: </span>
                {turn.content}
              </p>
            ) : (
              <AssistantMessage answer={turn.answer} fallback={turn.content} />
            )}
          </li>
        ))}
      </ol>

      {sending ? <p className="muted" role="status">Doradca sprawdza dane…</p> : null}
      {error !== null ? (
        <p role="alert" id={ids.error} className="notice">
          {error}
        </p>
      ) : null}

      {full ? (
        <p role="status" id={ids.limit} className="notice">
          Ta rozmowa osiągnęła limit. Zacznij nową rozmowę, aby zadać kolejne pytanie.
        </p>
      ) : null}

      <form onSubmit={onSubmit} noValidate className="ai-chat__form">
        <p className="field-hint">Pola oznaczone * są wymagane.</p>
        <div className="field">
          <label htmlFor={ids.input}>
            Twoja wiadomość<span aria-hidden="true"> *</span>
          </label>
          <textarea
            id={ids.input}
            rows={3}
            maxLength={TURN_MAX}
            value={draft}
            readOnly={full}
            required
            aria-invalid={draftInvalid ? true : undefined}
            aria-describedby={[draftInvalid ? ids.error : null, full ? ids.limit : null].filter(Boolean).join(" ") || undefined}
            onChange={(event) => setDraft(event.target.value)}
          />
          {draft.length >= TURN_MAX * 0.8 ? (
            <p className="field-hint" aria-live="polite">
              {draft.length}/{TURN_MAX} znaków
            </p>
          ) : null}
        </div>
        <div className="ai-chat__actions">
          <button type="submit" className="button" aria-disabled={sending || full ? true : undefined}>
            {sending ? "Wysyłanie…" : "Wyślij"}
          </button>
          <button type="button" className="link-button" onClick={startOver}>
            Nowa rozmowa
          </button>
        </div>
      </form>
    </section>
  );
}

function AssistantMessage({ answer, fallback }: { readonly answer: AgentReply | undefined; readonly fallback: string }) {
  if (answer === undefined) return <p>{fallback}</p>;
  const stop = stopReasonText(answer.stopReason);
  return (
    <div className="ai-box">
      <p className="ai-box__label">{answer.aiLabel || AI_LABEL}</p>
      {answer.steps.length > 0 ? (
        <ul className="ai-chat__steps muted">
          {answer.steps.map((step, index) => (
            <li key={`${index}-${step.tool}`}>Sprawdziłem: {step.summary}</li>
          ))}
        </ul>
      ) : null}
      <p className="ai-box__text">{answer.reply}</p>
      {stop !== "" ? <p className="notice">{stop}</p> : null}
      {answer.citedSlugs.length > 0 ? (
        <p>
          Szkoły w odpowiedzi:{" "}
          {answer.citedSlugs.filter(isSlug).map((slug, index) => (
            <span key={slug}>
              {index > 0 ? ", " : null}
              <Link href={`/szkoly/${slug}`}>profil szkoły ({slug})</Link>
            </span>
          ))}
        </p>
      ) : null}
    </div>
  );
}
