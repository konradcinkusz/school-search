"use client";

import { useEffect, useState } from "react";
import { bffRequest, errorCodeOf } from "@/lib/bff-client";
import { parseLeads } from "@/lib/billing-parse";
import type { LeadView } from "@/lib/billing-types";
import { formatDatePl } from "@/lib/format";

/**
 * Parent enquiries for the owner. Personal data: shown only here, to the owner, and deleted on request. The service
 * purges every enquiry 90 days after it arrived.
 */
export function EnquiryInbox({ slug }: { readonly slug: string }) {
  const [leads, setLeads] = useState<LeadView[] | null>(null);
  const [failed, setFailed] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    (async () => {
      const response = await bffRequest(
        `/api/proxy/api/v1/schools/${encodeURIComponent(slug)}/enquiries?markRead=true`,
      );
      if (!active) return;
      const parsed = response?.status === 200 ? parseLeads(response.body) : null;
      if (parsed === null) {
        setFailed("Skrzynki wiadomości są chwilowo niedostępne.");
        return;
      }
      setLeads(parsed);
    })();
    return () => {
      active = false;
    };
  }, [slug]);

  async function remove(id: string) {
    setNotice(null);
    const response = await bffRequest(`/api/proxy/api/v1/enquiries/${encodeURIComponent(id)}`, { method: "DELETE" });
    if (response !== null && (response.status === 200 || response.status === 204)) {
      setLeads((current) => (current ?? []).filter((lead) => lead.id !== id));
      setNotice("Wiadomość została usunięta.");
      return;
    }
    setNotice(errorCodeOf(response?.body) === "not_found" ? "Wiadomość już nie istnieje." : "Nie udało się usunąć wiadomości.");
  }

  return (
    <section aria-labelledby="inbox-heading">
      <h2 id="inbox-heading">Wiadomości od rodziców</h2>
      <p className="muted">
        Wiadomości widzisz tylko Ty. Nie są publikowane ani wysyłane marketingowo. Usuwamy je automatycznie po 90 dniach.
      </p>
      {notice !== null ? (
        <p role="status" className="notice">
          {notice}
        </p>
      ) : null}
      {failed !== null ? (
        <p className="notice" role="alert">
          {failed}
        </p>
      ) : leads === null ? (
        <p className="muted">Ładowanie…</p>
      ) : leads.length === 0 ? (
        <p className="muted">Brak wiadomości.</p>
      ) : (
        <ul className="plain-list">
          {leads.map((lead) => (
            <li key={lead.id} className="card">
              <p className="muted">
                {lead.status === "new" ? "Nowa" : "Przeczytana"}
                {formatDatePl(lead.createdAt) ? ` · ${formatDatePl(lead.createdAt)}` : ""}
              </p>
              <p>
                Adres do odpowiedzi: <span className="token">{lead.parentEmail}</span>
              </p>
              <p className="review__body">{lead.message}</p>
              <p>
                <button type="button" className="link-button" onClick={() => remove(lead.id)}>
                  Usuń wiadomość
                </button>
              </p>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
