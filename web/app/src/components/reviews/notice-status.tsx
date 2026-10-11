"use client";

import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, errorMessageOf, isUnavailable } from "@/lib/bff-client";
import type { NoticeStatusView, StatementOfReasons } from "@/lib/reviews-types";

/**
 * Status of a notice and its statement of reasons, read with the id and the acknowledgement token.
 * An appeal (art. 20) can be filed from the decision. Both calls go through the BFF proxy.
 */
const DECISION_ACTION: Record<string, string> = {
  remove: "treść usunięta",
  restrict: "treść ograniczona",
  keep: "treść pozostawiona bez zmian",
  publish: "opinia opublikowana",
  reject: "opinia odrzucona",
};

const STATUS_TEXT: Record<string, string> = {
  received: "przyjęte, w trakcie rozpatrywania",
  decided: "rozpatrzone",
};

export function NoticeStatus() {
  const ids = { id: useId(), token: useId(), appeal: useId() };
  const [sending, setSending] = useState(false);
  const [view, setView] = useState<NoticeStatusView | null>(null);
  const [token, setToken] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [appealDone, setAppealDone] = useState<string | null>(null);

  async function onLookup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const id = String(data.get("id") ?? "").trim();
    const ackToken = String(data.get("token") ?? "").trim();
    if (!/^[0-9a-fA-F-]{36}$/.test(id) || ackToken === "" || ackToken.length > 200) {
      setError("Podaj numer zgłoszenia i token potwierdzenia.");
      return;
    }
    setSending(true);
    setError(null);
    setAppealDone(null);
    const response = await bffRequest(
      `/api/proxy/api/v1/notices/${encodeURIComponent(id)}?token=${encodeURIComponent(ackToken)}`,
    );
    setSending(false);
    if (response === null || isUnavailable(response)) {
      setError("Usługa zgłoszeń jest chwilowo niedostępna.");
      return;
    }
    if (response.status === 200 && isStatusView(response.body)) {
      setView(response.body);
      setToken(ackToken);
      return;
    }
    setView(null);
    setError(
      response.status === 404 || errorCodeOf(response.body) === "not_found"
        ? "Nie znaleziono zgłoszenia o podanym numerze i tokenie."
        : (errorMessageOf(response.body) ?? "Nie udało się sprawdzić statusu zgłoszenia."),
    );
  }

  return (
    <div>
      <form onSubmit={onLookup} noValidate>
        <div className="field">
          <label htmlFor={ids.id}>Numer zgłoszenia</label>
          <input id={ids.id} name="id" type="text" maxLength={36} autoComplete="off" required />
        </div>
        <div className="field">
          <label htmlFor={ids.token}>Token potwierdzenia</label>
          <input id={ids.token} name="token" type="text" maxLength={200} autoComplete="off" required />
        </div>
        <button type="submit" className="button" disabled={sending}>
          {sending ? "Sprawdzanie…" : "Sprawdź status"}
        </button>
      </form>

      {error !== null ? (
        <p role="alert" className="notice">
          {error}
        </p>
      ) : null}

      {view !== null ? (
        <div className="card" role="status">
          <p>
            Status: <strong>{STATUS_TEXT[view.status] ?? view.status}</strong>
          </p>
          {view.decision ? (
            <>
              <DecisionDetails decision={view.decision} />
              <AppealForm decisionId={view.decision.id} token={token} onDone={setAppealDone} />
            </>
          ) : (
            <p className="muted">Decyzja nie została jeszcze podjęta.</p>
          )}
          {appealDone !== null ? <p className="notice">{appealDone}</p> : null}
        </div>
      ) : null}
    </div>
  );
}

function DecisionDetails({ decision }: { readonly decision: StatementOfReasons }) {
  return (
    <div className="decision">
      <p>
        <strong>Uzasadnienie decyzji</strong> ({DECISION_ACTION[decision.action] ?? decision.action})
      </p>
      <dl className="facts">
        <dt>Podstawa</dt>
        <dd>{decision.legalOrTermsGround}</dd>
        <dt>Okoliczności</dt>
        <dd>{decision.facts}</dd>
        <dt>Decyzja zautomatyzowana</dt>
        <dd>{decision.automated ? "tak" : "nie, decyzję podjął człowiek"}</dd>
        <dt>Odwołanie</dt>
        <dd>{decision.redressInfo}</dd>
      </dl>
    </div>
  );
}

function AppealForm({
  decisionId,
  token,
  onDone,
}: {
  readonly decisionId: string;
  readonly token: string;
  readonly onDone: (text: string) => void;
}) {
  const id = useId();
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const explanation = String(data.get("explanation") ?? "").trim();
    if (explanation.length < 20 || explanation.length > 3000) {
      setError("Uzasadnij odwołanie (od 20 do 3000 znaków).");
      return;
    }
    setSending(true);
    setError(null);
    const response = await bffRequest(`/api/proxy/api/v1/decisions/${encodeURIComponent(decisionId)}/complaints`, {
      method: "POST",
      body: { explanation, token },
    });
    setSending(false);
    if (response !== null && response.status === 201) {
      const ack = typeof response.body === "object" && response.body !== null ? (response.body as { ackToken?: unknown }).ackToken : undefined;
      onDone(
        typeof ack === "string"
          ? `Odwołanie przyjęte. Token odwołania: ${ack}. Zapisz go.`
          : "Odwołanie zostało przyjęte.",
      );
      return;
    }
    setError(errorMessageOf(response?.body) ?? "Nie udało się złożyć odwołania. Spróbuj ponownie.");
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={id}>Odwołanie od decyzji (art. 20 DSA): uzasadnij, dlaczego decyzja jest błędna</label>
        <textarea id={id} name="explanation" rows={4} minLength={20} maxLength={3000} required />
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Wysyłanie…" : "Złóż odwołanie"}
      </button>
      {error !== null ? <p role="alert" className="notice">{error}</p> : null}
    </form>
  );
}

function isStatusView(value: unknown): value is NoticeStatusView {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.id === "string" && typeof v.status === "string";
}
