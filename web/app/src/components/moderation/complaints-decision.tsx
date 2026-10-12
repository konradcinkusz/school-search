"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, isUnavailable, type BffResponse } from "@/lib/bff-client";

/**
 * Decision on an internal complaint (DSA art. 20). The Reviews queue does not list complaints yet, so the moderator
 * enters the complaint id from the complainant's receipt. The Reviews service re-checks the role and writes the
 * statement of reasons; nothing here is automatic.
 */

const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const FACTS_MIN = 10;
const FACTS_MAX = 3000;

type FieldKey = "complaint" | "facts";
type FieldErrors = Partial<Record<FieldKey, string>>;

const COPY = {
  complaint: "Podaj identyfikator skargi z potwierdzenia (format UUID).",
  facts: `Okoliczności: od ${FACTS_MIN} do ${FACTS_MAX} znaków.`,
  notFound: "Nie znaleziono skargi o tym identyfikatorze. Sprawdź go w potwierdzeniu.",
  forbidden: "Nie masz uprawnień do zapisania decyzji w skardze.",
  invalid: "Sprawdź wynik skargi i okoliczności, a potem zapisz decyzję ponownie.",
  unavailable: "Usługa opinii jest chwilowo niedostępna. Decyzja nie została zapisana.",
  generic: "Nie udało się zapisać decyzji. Spróbuj ponownie.",
};

function failureText(response: BffResponse | null): string {
  if (response === null || isUnavailable(response)) return COPY.unavailable;
  if (response.status === 403 || response.status === 401) return COPY.forbidden;
  if (response.status === 404) return COPY.notFound;
  if (response.status === 400) return COPY.invalid;
  return COPY.generic;
}

export function ComplaintDecisionForm() {
  const ids = {
    complaint: useId(),
    complaintError: useId(),
    outcome: useId(),
    facts: useId(),
    factsError: useId(),
  };
  const [sending, setSending] = useState(false);
  const [fields, setFields] = useState<FieldErrors>({});
  const [feedback, setFeedback] = useState<{ kind: "error" | "done"; text: string } | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const complaintId = String(data.get("complaint") ?? "").trim();
    const outcome = String(data.get("outcome") ?? "");
    const facts = String(data.get("facts") ?? "").trim();

    const invalid: FieldErrors = {};
    if (!UUID_PATTERN.test(complaintId)) invalid.complaint = COPY.complaint;
    if (facts.length < FACTS_MIN || facts.length > FACTS_MAX) invalid.facts = COPY.facts;
    if (invalid.complaint !== undefined || invalid.facts !== undefined) {
      setFields(invalid);
      setFeedback(null);
      document.getElementById(invalid.complaint !== undefined ? ids.complaint : ids.facts)?.focus();
      return;
    }

    setSending(true);
    setFields({});
    setFeedback(null);
    const response = await bffRequest(`/api/proxy/api/v1/moderation/complaints/${encodeURIComponent(complaintId)}/decision`, {
      method: "POST",
      body: { outcome, facts },
    });
    setSending(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      setFeedback({ kind: "done", text: "Decyzja w skardze zapisana. Uzasadnienie trafi do zainteresowanych." });
      form.reset();
      return;
    }
    setFeedback({ kind: "error", text: failureText(response) });
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <p className="muted">
        Lista skarg nie jest jeszcze w kolejce moderacji. Wpisz identyfikator skargi, który zgłaszający dostał po jej
        złożeniu.
      </p>
      <p className="field-hint">Pola oznaczone * są wymagane.</p>
      <div className="field">
        <label htmlFor={ids.complaint}>
          Identyfikator skargi<span aria-hidden="true"> *</span>
        </label>
        <input
          id={ids.complaint}
          name="complaint"
          type="text"
          maxLength={36}
          autoComplete="off"
          required
          aria-invalid={fields.complaint ? true : undefined}
          aria-describedby={fields.complaint ? ids.complaintError : undefined}
        />
        {fields.complaint ? (
          <p id={ids.complaintError} className="field-error">
            {fields.complaint}
          </p>
        ) : null}
      </div>
      <div className="field">
        <label htmlFor={ids.outcome}>Wynik skargi</label>
        <select id={ids.outcome} name="outcome" defaultValue="upheld">
          <option value="upheld">Skarga uwzględniona</option>
          <option value="reversed">Skarga odrzucona (decyzja utrzymana)</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.facts}>
          Okoliczności faktyczne i uzasadnienie<span aria-hidden="true"> *</span>
        </label>
        <textarea
          id={ids.facts}
          name="facts"
          rows={4}
          maxLength={FACTS_MAX}
          required
          aria-invalid={fields.facts ? true : undefined}
          aria-describedby={fields.facts ? ids.factsError : undefined}
        />
        {fields.facts ? (
          <p id={ids.factsError} className="field-error">
            {fields.facts}
          </p>
        ) : null}
      </div>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Zapisywanie…" : "Zapisz decyzję w skardze"}
      </button>
      {feedback !== null ? (
        <p role={feedback.kind === "error" ? "alert" : "status"} className="notice">
          {feedback.text}
        </p>
      ) : null}
    </form>
  );
}
