"use client";

import Link from "next/link";
import { useEffect, useId, useState, type FormEvent } from "react";
import { SessionGate } from "@/components/billing/session-gate";
import { parseSchoolReviewsForOwner, type SchoolReviewForOwner } from "@/lib/ai-contract";
import { bffRequest, type BffResponse } from "@/lib/bff-client";

/**
 * Right of reply for the school's representative (SPEC-E): a reply to a published review, published only after
 * moderation. The Reviews service checks that the signed-in user is the verified representative of this school.
 */

const BODY_MIN = 10;
const BODY_MAX = 2000;
const LABEL_MAX = 120;

function replyFailureText(response: BffResponse | null): string {
  if (response === null || response.status === 503 || response.status === 504) {
    return "Opinie są chwilowo niedostępne. Odpowiedź nie została wysłana.";
  }
  if (response.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
  if (response.status === 403) return "Nie masz uprawnień do odpowiedzi w imieniu tej szkoły.";
  if (response.status === 409) return "Ta opinia ma już odpowiedź.";
  if (response.status === 400 || response.status === 422) {
    return `Sprawdź dane odpowiedzi: treść od ${BODY_MIN} do ${BODY_MAX} znaków, rola do ${LABEL_MAX} znaków.`;
  }
  return "Nie udało się wysłać odpowiedzi. Sprawdź dane i spróbuj ponownie.";
}

export function OwnerRepliesPanel({ slug }: { readonly slug: string }) {
  return (
    <section className="card" aria-labelledby="owner-replies-heading">
      <h2 id="owner-replies-heading">Odpowiedzi na opinie</h2>
      <p className="muted">
        Możesz odpowiedzieć na opinie o szkole. Odpowiedź pojawi się publicznie po sprawdzeniu przez moderatora i jest
        oznaczona jako odpowiedź szkoły. Nie usuwa ani nie zmienia opinii.
      </p>
      <SessionGate requireVerified>
        <OwnerReviews slug={slug} />
      </SessionGate>
    </section>
  );
}

function OwnerReviews({ slug }: { readonly slug: string }) {
  const [reviews, setReviews] = useState<SchoolReviewForOwner[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest(`/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/reviews?page=1&pageSize=20`);
      if (!active) return;
      const parsed = response?.status === 200 ? parseSchoolReviewsForOwner(response.body) : null;
      if (parsed === null) {
        setFailed(true);
        return;
      }
      setFailed(false);
      setReviews(parsed);
    })();
    return () => {
      active = false;
    };
  }, [slug, reloadKey]);

  if (failed) {
    return (
      <p className="notice" role="alert">
        Opinie są chwilowo niedostępne.
      </p>
    );
  }
  if (reviews === null) return <p className="muted">Ładowanie opinii…</p>;

  const open = reviews.filter((review) => !review.hasReply);
  if (open.length === 0) {
    return (
      <p className="muted">
        Nie ma opinii bez odpowiedzi. Odpowiedzi oczekujące na moderację nie są tu widoczne. Ich stan zobaczysz na{" "}
        <Link href="/konto">stronie konta</Link>.
      </p>
    );
  }

  return (
    <ul className="plain-list">
      {open.map((review) => (
        <li key={review.id} className="review">
          <p className="muted">Ocena autora: {review.rating} z 5</p>
          {review.title ? <h3 className="review__title">{review.title}</h3> : null}
          {review.body ? <p className="review__body">{review.body}</p> : null}
          <ReplyForm reviewId={review.id} onSent={() => setReloadKey((key) => key + 1)} />
        </li>
      ))}
    </ul>
  );
}

function ReplyForm({ reviewId, onSent }: { readonly reviewId: string; readonly onSent: () => void }) {
  const ids = { label: useId(), body: useId() };
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const representativeLabel = String(data.get("label") ?? "").trim();
    const body = String(data.get("body") ?? "").trim();
    if (representativeLabel === "" || representativeLabel.length > LABEL_MAX) {
      setError(`Podaj, w jakiej roli odpowiadasz (do ${LABEL_MAX} znaków).`);
      return;
    }
    if (body.length < BODY_MIN || body.length > BODY_MAX) {
      setError(`Odpowiedź ma mieć od ${BODY_MIN} do ${BODY_MAX} znaków.`);
      return;
    }
    setSending(true);
    setError(null);
    const response = await bffRequest(`/api/proxy/api/v1/reviews/${encodeURIComponent(reviewId)}/reply`, {
      method: "POST",
      body: { body, representativeLabel },
    });
    setSending(false);
    if (response !== null && response.status >= 200 && response.status < 300) {
      setDone(true);
      onSent();
      return;
    }
    setError(replyFailureText(response));
  }

  if (done) {
    return (
      <p className="notice" role="status">
        Odpowiedź wysłana i czeka na sprawdzenie przez moderatora.
      </p>
    );
  }

  return (
    <form onSubmit={onSubmit} noValidate className="review-form">
      <div className="field">
        <label htmlFor={ids.label}>Twoja rola w szkole (na przykład „dyrektor”)</label>
        <input id={ids.label} name="label" type="text" maxLength={LABEL_MAX} required />
      </div>
      <div className="field">
        <label htmlFor={ids.body}>Treść odpowiedzi</label>
        <textarea id={ids.body} name="body" rows={4} maxLength={BODY_MAX} required />
      </div>
      <button type="submit" className="button" disabled={sending}>
        {sending ? "Wysyłanie…" : "Wyślij odpowiedź"}
      </button>
      {error !== null ? (
        <p role="alert" className="notice">
          {error}
        </p>
      ) : null}
    </form>
  );
}
