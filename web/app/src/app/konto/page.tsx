import type { Metadata } from "next";
import { AccountPanel } from "@/components/account/account-panel";

// Client-driven: the session is read through the BFF on the client. Nothing sensitive is rendered on the server.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Konto",
  description: "Twoje konto: status opinii i uzasadnienia decyzji moderacji.",
  robots: { index: false, follow: false },
  alternates: { canonical: "/konto" },
};

export default function AccountPage() {
  return (
    <article>
      <h1>Konto</h1>
      <AccountPanel />
    </article>
  );
}
