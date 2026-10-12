import Link from "next/link";
import type { Metadata } from "next";
import "./styles/static.css";

export const metadata: Metadata = {
  title: "Nie znaleziono strony",
};

const POPULAR = [
  { href: "/ranking/country", label: "Ranking szkół" },
  { href: "/przedszkola", label: "Przedszkola" },
  { href: "/porownaj", label: "Porównaj szkoły" },
  { href: "/metodologia", label: "Jak liczymy wyniki" },
  { href: "/dla-szkol", label: "Dla szkół" },
];

export default function NotFound() {
  return (
    <section className="empty-state">
      <svg className="empty-state__art" viewBox="0 0 160 120" aria-hidden="true" focusable="false">
        <path className="empty-state__ground" d="M10 108h140" />
        <path className="empty-state__post" d="M80 108V22" />
        <path className="empty-state__sign" d="M36 30h58l10 10-10 10H36z" />
        <path className="empty-state__sign empty-state__sign--alt" d="M124 58H68l-10 10 10 10h56z" />
        <circle className="empty-state__dot" cx="80" cy="16" r="5" />
      </svg>
      <p className="eyebrow">Błąd 404</p>
      <h1>Nie znaleziono strony</h1>
      <p className="lead">Adres nie istnieje albo został zmieniony.</p>
      <p className="actions">
        <Link href="/" className="button">
          Wróć na stronę główną
        </Link>
      </p>
      <nav aria-label="Popularne strony">
        <p className="muted">Może szukasz jednej z tych stron:</p>
        <ul className="plain-list link-list">
          {POPULAR.map((item) => (
            <li key={item.href}>
              <Link href={item.href}>{item.label}</Link>
            </li>
          ))}
        </ul>
      </nav>
    </section>
  );
}
