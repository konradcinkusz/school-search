import Link from "next/link";

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
        <ul className="plain-list">
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
