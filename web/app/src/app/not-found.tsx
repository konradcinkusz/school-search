import Link from "next/link";

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
    </section>
  );
}
