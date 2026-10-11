import Link from "next/link";

export default function NotFound() {
  return (
    <section>
      <h1>Nie znaleziono strony</h1>
      <p>Adres nie istnieje albo został zmieniony.</p>
      <p>
        <Link href="/">Wróć na stronę główną</Link>
      </p>
    </section>
  );
}
