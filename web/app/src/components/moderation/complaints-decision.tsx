"use client";

import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorMessageOf, isUnavailable } from "@/lib/bff-client";

/**
 * Decision on an internal complaint (DSA art. 20). The Reviews queue does not list complaints yet, so the moderator
 * enters the complaint id from the complainant's receipt. The Reviews service re-checks the role and writes the
 * statement of reasons; nothing here is automatic.
 */

const UUID_PATTERN = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const FACTS_MIN = 10;
const FACTS_MAX = 3000;

export function ComplaintDecisionForm() {
  const ids = { complaint: useId(), outcome: useId(), facts: useId() };
  const [sending, setSending] = useState(false);
  const [feedback, setFeedback] = useState<{ kind: "error" | "done"; text: string } | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    const complaintId = String(data.get("complaint") ?? "").trim();
    const outcome = String(data.get("outcome") ?? "");
    const facts = String(data.get("facts") ?? "").trim();

    if (!UUID_PATTERN.test(complaintId)) {
      setFeedback({ kind: "error", text: "Podaj identyfikator skargi z potwierdzenia (format UUID)." });
      return;
    }
    if (facts.length < FACTS_MIN || facts.length > FACTS_MAX) {
      setFeedback({ kind: "error", text: `Okoliczności: od ${FACTS_MIN} do ${FACTS_MAX} znaków.` });
      return;
    }

    setSending(true);
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
    if (response === null || isUnavailable(response)) {
      setFeedback({ kind: "error", text: "Usługa opinii jest chwilowo niedostępna. Decyzja nie została zapisana." });
      return;
    }
    setFeedback({ kind: "error", text: errorMessageOf(response.body) ?? "Nie udało się zapisać decyzji." });
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <p className="muted">
        Lista skarg nie jest jeszcze w kolejce moderacji. Wpisz identyfikator skargi, który zgłaszający dostał po jej
        złożeniu.
      </p>
      <div className="field">
        <label htmlFor={ids.complaint}>Identyfikator skargi</label>
        <input id={ids.complaint} name="complaint" type="text" maxLength={36} autoComplete="off" required />
      </div>
      <div className="field">
        <label htmlFor={ids.outcome}>Wynik skargi</label>
        <select id={ids.outcome} name="outcome" defaultValue="upheld">
          <option value="upheld">Skarga uwzględniona</option>
          <option value="reversed">Skarga odrzucona (decyzja utrzymana)</option>
        </select>
      </div>
      <div className="field">
        <label htmlFor={ids.facts}>Okoliczności faktyczne i uzasadnienie</label>
        <textarea id={ids.facts} name="facts" rows={4} maxLength={FACTS_MAX} required />
      </div>
      <button type="submit" className="button" disabled={sending}>
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
