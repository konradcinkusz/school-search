"use client";

import "@/app/styles/forms.css";
import { useId, useState, type FormEvent } from "react";
import { bffRequest, errorCodeOf, isUnavailable, type BffResponse } from "@/lib/bff-client";
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

const LOOKUP_COPY = {
  id: "Podaj numer zgłoszenia z potwierdzenia, w pełnym formacie.",
  token: "Podaj token potwierdzenia z potwierdzenia zgłoszenia.",
  unavailable: "Usługa zgłoszeń jest chwilowo niedostępna.",
  notFound: "Nie znaleziono zgłoszenia o podanym numerze i tokenie.",
  generic: "Nie udało się sprawdzić statusu zgłoszenia. Spróbuj ponownie.",
};

const APPEAL_COPY = {
  explanation: "Uzasadnij odwołanie (od 20 do 3000 znaków).",
  unavailable: "Usługa odwołań jest chwilowo niedostępna. Odwołanie nie zostało złożone.",
  rateLimited: "Zbyt wiele prób. Spróbuj ponownie później.",
  generic: "Nie udało się złożyć odwołania. Sprawdź treść i spróbuj ponownie.",
};

export function NoticeStatus() {
  const ids = {
    id: useId(),
    idError: useId(),
    token: useId(),
    tokenError: useId(),
    appeal: useId(),
  };
  const [sending, setSending] = useState(false);
  const [view, setView] = useState<NoticeStatusView | null>(null);
  const [token, setToken] = useState("");
  const [lookupErrors, setLookupErrors] = useState<{ id?: string; token?: string }>({});
  const [error, setError] = useState<string | null>(null);
  const [appealDone, setAppealDone] = useState<string | null>(null);

  async function onLookup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const id = String(data.get("id") ?? "").trim();
    const ackToken = String(data.get("token") ?? "").trim();
    const invalid: { id?: string; token?: string } = {};
    if (!/^[0-9a-fA-F-]{36}$/.test(id)) invalid.id = LOOKUP_COPY.id;
    if (ackToken === "" || ackToken.length > 200) invalid.token = LOOKUP_COPY.token;
    if (invalid.id !== undefined || invalid.token !== undefined) {
      setLookupErrors(invalid);
      setError(null);
      document.getElementById(invalid.id !== undefined ? ids.id : ids.token)?.focus();
      return;
    }
    setLookupErrors({});
    setSending(true);
    setError(null);
    setAppealDone(null);
    const response = await bffRequest(
      `/api/proxy/api/v1/notices/${encodeURIComponent(id)}?token=${encodeURIComponent(ackToken)}`,
    );
    setSending(false);
    if (response === null || isUnavailable(response)) {
      setError(LOOKUP_COPY.unavailable);
      return;
    }
    if (response.status === 200 && isStatusView(response.body)) {
      setView(response.body);
      setToken(ackToken);
      return;
    }
    setView(null);
    setError(response.status === 404 || errorCodeOf(response.body) === "not_found" ? LOOKUP_COPY.notFound : LOOKUP_COPY.generic);
  }

  return (
    <div>
      <form onSubmit={onLookup} noValidate>
        <p className="field-hint">Pola oznaczone * są wymagane.</p>
        <div className="field">
          <label htmlFor={ids.id}>
            Numer zgłoszenia<span aria-hidden="true"> *</span>
          </label>
          <input
            id={ids.id}
            name="id"
            type="text"
            maxLength={36}
            autoComplete="off"
            required
            aria-invalid={lookupErrors.id ? true : undefined}
            aria-describedby={lookupErrors.id ? ids.idError : undefined}
          />
          {lookupErrors.id ? (
            <p id={ids.idError} className="field-error">
              {lookupErrors.id}
            </p>
          ) : null}
        </div>
        <div className="field">
          <label htmlFor={ids.token}>
            Token potwierdzenia<span aria-hidden="true"> *</span>
          </label>
          <input
            id={ids.token}
            name="token"
            type="text"
            maxLength={200}
            autoComplete="off"
            required
            aria-invalid={lookupErrors.token ? true : undefined}
            aria-describedby={lookupErrors.token ? ids.tokenError : undefined}
          />
          {lookupErrors.token ? (
            <p id={ids.tokenError} className="field-error">
              {lookupErrors.token}
            </p>
          ) : null}
        </div>
        <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
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

function appealFailureText(response: BffResponse | null): string {
  if (response === null || response.status === 503 || response.status === 504 || response.status === 502) {
    return APPEAL_COPY.unavailable;
  }
  if (response.status === 429) return APPEAL_COPY.rateLimited;
  return APPEAL_COPY.generic;
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
  const ids = { explanation: useId(), explanationError: useId() };
  const [sending, setSending] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const explanation = String(data.get("explanation") ?? "").trim();
    if (explanation.length < 20 || explanation.length > 3000) {
      setFieldError(APPEAL_COPY.explanation);
      setError(null);
      document.getElementById(ids.explanation)?.focus();
      return;
    }
    setFieldError(null);
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
    setError(appealFailureText(response));
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="field">
        <label htmlFor={ids.explanation}>
          Odwołanie od decyzji (art. 20 DSA): uzasadnij, dlaczego decyzja jest błędna<span aria-hidden="true"> *</span>
        </label>
        <textarea
          id={ids.explanation}
          name="explanation"
          rows={4}
          minLength={20}
          maxLength={3000}
          required
          aria-invalid={fieldError ? true : undefined}
          aria-describedby={fieldError ? ids.explanationError : undefined}
        />
        {fieldError ? (
          <p id={ids.explanationError} className="field-error">
            {fieldError}
          </p>
        ) : null}
      </div>
      <button type="submit" className="button" aria-disabled={sending ? true : undefined}>
        {sending ? "Wysyłanie…" : "Złóż odwołanie"}
      </button>
      {error !== null ? (
        <p role="alert" className="notice">
          {error}
        </p>
      ) : null}
    </form>
  );
}

function isStatusView(value: unknown): value is NoticeStatusView {
  if (typeof value !== "object" || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.id === "string" && typeof v.status === "string";
}
