import type { Metadata } from "next";
import { ClaimsOverview } from "@/components/billing/owner-claims";
import { SessionGate } from "@/components/billing/session-gate";
import "../../styles/static.css";

// Client-driven: the session and the claims are read through the BFF on the client.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Konto szkoły",
  description: "Zgłoszenia reprezentacji szkół i stan profili rozszerzonych.",
  robots: { index: false, follow: false },
  alternates: { canonical: "/konto/szkola" },
};

export default function SchoolAccountPage() {
  return (
    <article>
      <h1>Konto szkoły</h1>
      <SessionGate requireVerified>
        <ClaimsOverview />
      </SessionGate>
    </article>
  );
}
