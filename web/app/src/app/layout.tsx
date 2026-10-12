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
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#ffffff" },
    { media: "(prefers-color-scheme: dark)", color: "#1b1e23" },
  ],
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="pl">
      <body>
        <a href="#main" className="skip-link">
          Przejdź do treści
        </a>
        <SiteHeader />
        <main id="main" className="site-main" tabIndex={-1}>
          {children}
        </main>
        <footer className="site-footer">
          <div className="site-footer__inner">
            <div>
              <p className="site-footer__brand">Wyszukiwarka szkół</p>
              <p>Dane: publiczne zbiory danych oświatowych. Wyniki mają charakter orientacyjny.</p>
              <p>Projekt niezależny. Nie jest oficjalnym rankingiem żadnego organu.</p>
            </div>
            <nav aria-label="Serwis">
              <p className="site-footer__heading">Serwis</p>
              <ul className="site-footer__links">
                <li>
                  <Link href="/metodologia">Metodologia</Link>
                </li>
                <li>
                  <Link href="/dla-szkol">Dla szkół</Link>
                </li>
                <li>
                  <Link href="/cennik">Cennik</Link>
                </li>
              </ul>
            </nav>
            <nav aria-label="Informacje prawne">
              <p className="site-footer__heading">Informacje prawne</p>
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
