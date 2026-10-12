import Link from "next/link";

/** Shown when the catalog cannot be reached. Never a blank page and never an invented value. */
export function ServiceUnavailable({ what }: { what: string }) {
  return (
    <div className="notice" role="alert">
      <p>
        <strong>Usługa jest chwilowo niedostępna.</strong> Nie udało się pobrać: {what}.
      </p>
      <p>Spróbuj ponownie za chwilę. Nie pokazujemy wyników, których nie możemy sprawdzić.</p>
      <p className="actions">
        <Link href="/" className="button button--secondary">
          Wróć do wyszukiwarki
        </Link>
      </p>
    </div>
  );
}
