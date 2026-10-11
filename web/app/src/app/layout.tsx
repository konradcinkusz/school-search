import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import "./globals.css";
import Link from "next/link";
import { SiteHeader } from "@/components/site-header";

export const metadata: Metadata = {
  title: {
    default: "Wyszukiwarka szkół podstawowych",
    template: "%s | Wyszukiwarka szkół",
  },
  description:
    "Wyszukiwarka szkół podstawowych z wynikami, poziomem pewności i metodologią. Nie jest oficjalnym rankingiem.",
  applicationName: "Wyszukiwarka szkół",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="pl">
      <body>
        <a href="#main" className="muted">
          Przejdź do treści
        </a>
        <SiteHeader />
        <main id="main" className="site-main">
          {children}
        </main>
        <footer className="site-footer">
          <div className="site-footer__inner">
            <p>Dane: publiczne zbiory danych oświatowych. Wyniki mają charakter orientacyjny.</p>
            <p>Projekt niezależny. Nie jest oficjalnym rankingiem żadnego organu.</p>
            <nav aria-label="Informacje prawne">
              <ul className="site-footer__links">
                <li>
                  <Link href="/regulamin">Regulamin</Link>
                </li>
                <li>
                  <Link href="/prywatnosc">Prywatność</Link>
                </li>
                <li>
                  <Link href="/zglos-tresc">Zgłoś treść</Link>
                </li>
                <li>
                  <Link href="/kontakt">Kontakt</Link>
                </li>
              </ul>
            </nav>
          </div>
        </footer>
      </body>
    </html>
  );
}
