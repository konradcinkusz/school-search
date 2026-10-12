"use client";

import Link from "next/link";
import { useEffect, useState, type ReactNode } from "react";
import { bffRequest, isUnavailable } from "@/lib/bff-client";

/**
 * Shows its children only to a signed-in user (and, with requireVerified, one with a confirmed e-mail address).
 * This is UX gating: the Billing service re-checks every call.
 */
type Gate = "loading" | "unavailable" | "anonymous" | "unverified" | "ready";

export function SessionGate({
  children,
  requireVerified = false,
}: {
  readonly children: ReactNode;
  readonly requireVerified?: boolean;
}) {
  const [gate, setGate] = useState<Gate>("loading");

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest("/api/auth/session");
      if (!active) return;
      if (response === null || isUnavailable(response) || response.status !== 200) {
        setGate("unavailable");
        return;
      }
      const data = response.body as { authenticated?: unknown; emailVerified?: unknown };
      if (data.authenticated !== true) setGate("anonymous");
      else setGate(data.emailVerified === true ? "ready" : "unverified");
    })();
    return () => {
      active = false;
    };
  }, []);

  if (gate === "loading") return <p className="muted">Sprawdzanie logowania…</p>;
  if (gate === "unavailable") {
    return (
      <div className="notice notice--caution" role="status">
        <p>
          <strong>Konta są chwilowo niedostępne.</strong> Zarządzanie profilem szkoły wymaga konta. Przeglądanie szkół i
          wyników działa bez konta.
        </p>
        <p className="actions">
          <Link href="/">Wróć do wyszukiwarki</Link>
        </p>
      </div>
    );
  }
  if (gate === "anonymous") {
    return (
      <p className="notice">
        <Link href="/konto/logowanie">Zaloguj się</Link>, aby zarządzać profilem szkoły. Nie masz konta?{" "}
        <Link href="/konto/rejestracja">Załóż konto</Link>.
      </p>
    );
  }
  if (gate === "unverified" && requireVerified) {
    return (
      <p className="notice">
        Potwierdź adres e-mail (link w wiadomości od nas), a potem będziesz mógł zgłosić reprezentację szkoły. Stan konta
        sprawdzisz na <Link href="/konto">stronie konta</Link>.
      </p>
    );
  }
  return <>{children}</>;
}
