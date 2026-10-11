"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { bffRequest, errorCodeOf, isUnavailable, type BffResponse } from "@/lib/bff-client";
import { formatDatePl } from "@/lib/format";
import type { MyReview, StatementOfReasons } from "@/lib/reviews-types";

/** The signed-in area: own reviews with status, statements of reasons (DSA art. 17), withdrawal and sign-out. */

const REVIEW_STATUS: Record<string, string> = {
  pending: "oczekuje na sprawdzenie",
  published: "opublikowana",
  rejected: "odrzucona",
  removed: "usunięta",
};

const DECISION_ACTION: Record<string, string> = {
  remove: "treść usunięta",
  restrict: "treść ograniczona",
  keep: "treść pozostawiona",
  publish: "opinia opublikowana",
  reject: "opinia odrzucona",
};

type SessionState = "loading" | "unavailable" | "anonymous" | "unverified" | "ready";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function parseMine(body: unknown): MyReview[] | null {
  const list = Array.isArray(body) ? body : isRecord(body) && Array.isArray(body.items) ? body.items : null;
  if (list === null) return null;
  return list.filter(isRecord).map((item) => item as unknown as MyReview);
}

export function AccountPanel() {
  const router = useRouter();
  const [state, setState] = useState<SessionState>("loading");
  const [reviews, setReviews] = useState<MyReview[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  async function loadReviews(): Promise<void> {
    const response: BffResponse | null = await bffRequest("/api/proxy/api/v1/reviews/mine");
    if (response === null || isUnavailable(response)) {
      setLoadFailed(true);
      return;
    }
    if (response.status === 401) {
      setState("anonymous");
      return;
    }
    const parsed = response.status === 200 ? parseMine(response.body) : null;
    if (parsed === null) {
      setLoadFailed(true);
      return;
    }
    setReviews(parsed);
  }

  useEffect(() => {
    let active = true;
    (async () => {
      const session = await bffRequest("/api/auth/session");
      if (!active) return;
      if (session === null || isUnavailable(session) || session.status !== 200) {
        setState("unavailable");
        return;
      }
      const data = session.body as { authenticated?: unknown; emailVerified?: unknown };
      if (data.authenticated !== true) {
        setState("anonymous");
        return;
      }
      setState(data.emailVerified === true ? "ready" : "unverified");
      await loadReviews();
    })();
    return () => {
      active = false;
    };
  }, []);

  async function signOut() {
    await bffRequest("/api/auth/logout", { method: "POST" });
    router.push("/");
    router.refresh();
  }

  async function withdraw(id: string) {
    setNotice(null);
    const response = await bffRequest(`/api/proxy/api/v1/reviews/${encodeURIComponent(id)}`, { method: "DELETE" });
    if (response !== null && (response.status === 200 || response.status === 204)) {
      setNotice("Opinia została wycofana.");
      await loadReviews();
      return;
    }
    setNotice(errorCodeOf(response?.body) === "unauthenticated" ? "Sesja wygasła. Zaloguj się ponownie." : "Nie udało się wycofać opinii. Spróbuj ponownie.");
  }

  if (state === "loading") return <p className="muted">Ładowanie…</p>;
  if (state === "unavailable") {
    return (
      <div className="notice" role="alert">
        <p>
          <strong>Konta są chwilowo niedostępne.</strong>
        </p>
      </div>
    );
  }
  if (state === "anonymous") {
    return (
      <div>
        <p>Nie jesteś zalogowany.</p>
        <p>
          <Link href="/konto/logowanie">Zaloguj się</Link> · <Link href="/konto/rejestracja">Załóż konto</Link>
        </p>
      </div>
    );
  }

  return (
    <div>
      <p>
        {state === "unverified"
          ? "Adres e-mail nie został jeszcze potwierdzony. Opinie możesz pisać po potwierdzeniu adresu."
          : "Jesteś zalogowany z potwierdzonym adresem e-mail."}
      </p>
      <p>
        <button type="button" className="button" onClick={signOut}>
          Wyloguj się
        </button>
      </p>

      <section aria-labelledby="mine-heading">
        <h2 id="mine-heading">Moje opinie</h2>
        {notice !== null ? (
          <p role="status" className="notice">
            {notice}
          </p>
        ) : null}
        {loadFailed ? (
          <div className="notice" role="alert">
            <p>Nie udało się pobrać Twoich opinii. Spróbuj ponownie za chwilę.</p>
          </div>
        ) : reviews === null ? (
          <p className="muted">Ładowanie…</p>
        ) : reviews.length === 0 ? (
          <p className="muted">Nie dodałeś jeszcze żadnej opinii.</p>
        ) : (
          <ul className="plain-list">
            {reviews.map((review) => (
              <li key={review.id} className="card">
                <p className="muted">
                  Status: {REVIEW_STATUS[review.status] ?? review.status}
                  {review.createdAt && formatDatePl(review.createdAt) ? ` · dodano ${formatDatePl(review.createdAt)}` : ""}
                </p>
                <h3>{review.title}</h3>
                <p className="review__body">{review.body}</p>
                <p>
                  <Link href={`/szkoly/${encodeURIComponent(review.schoolSlug)}#opinie`}>Profil szkoły</Link>
                  {review.status === "pending" || review.status === "published" ? (
                    <>
                      {" · "}
                      <button type="button" className="link-button" onClick={() => withdraw(review.id)}>
                        Wycofaj opinię
                      </button>
                    </>
                  ) : null}
                </p>
                {review.decision ? <DecisionView decision={review.decision} /> : null}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function DecisionView({ decision }: { decision: StatementOfReasons }) {
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
