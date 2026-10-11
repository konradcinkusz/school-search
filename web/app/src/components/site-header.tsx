import Link from "next/link";

export function SiteHeader() {
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link href="/" className="site-header__brand">
          Wyszukiwarka szkół
        </Link>
        <nav className="site-nav" aria-label="Główna">
          <ul>
            <li>
              <Link href="/ranking/country">Ranking</Link>
            </li>
            <li>
              <Link href="/przedszkola">Przedszkola</Link>
            </li>
            <li>
              <Link href="/porownaj">Porównaj</Link>
            </li>
            <li>
              <Link href="/metodologia">Metodologia</Link>
            </li>
            <li>
              <Link href="/dla-szkol">Dla szkół</Link>
            </li>
          </ul>
        </nav>
      </div>
    </header>
  );
}
