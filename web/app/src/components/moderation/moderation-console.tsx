"use client";

import { useState } from "react";
import { ClaimsQueueView } from "@/components/moderation/claims-queue";
import { ComplaintDecisionForm } from "@/components/moderation/complaints-decision";
import { CorrectionsQueueView } from "@/components/moderation/corrections-queue";
import { ModerationQueueView } from "@/components/moderation/moderation-queue";

/** Moderator console: reviews and notices, school claims (Phase F), corrections with AI triage and complaints (Phase G). */
const TABS = [
  { id: "reviews", label: "Opinie i zgłoszenia" },
  { id: "claims", label: "Zgłoszenia właścicieli" },
  { id: "corrections", label: "Korekty" },
  { id: "complaints", label: "Skargi" },
] as const;

export function ModerationConsole() {
  const [tab, setTab] = useState<(typeof TABS)[number]["id"]>("reviews");
  return (
    <div>
      <div role="tablist" aria-label="Kolejki moderacji" className="tabs">
        {TABS.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            id={`tab-${item.id}`}
            aria-selected={tab === item.id}
            aria-controls={`panel-${item.id}`}
            className={tab === item.id ? "tabs__tab tabs__tab--active" : "tabs__tab"}
            onClick={() => setTab(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === "reviews" ? <ModerationQueueView /> : null}
        {tab === "claims" ? <ClaimsQueueView /> : null}
        {tab === "corrections" ? <CorrectionsQueueView /> : null}
        {tab === "complaints" ? <ComplaintDecisionForm /> : null}
      </div>
    </div>
  );
}
