import Link from "next/link";
import { NavLinks } from "@/components/nav-links";

/** Decorative mark: a school building. Hidden from assistive technology; the link text names the site. */
function BrandMark() {
  return (
    <span className="site-header__mark" aria-hidden="true">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" focusable="false">
        <path d="M3 10.5 12 5l9 5.5" />
        <path d="M5 9.5V19h14V9.5" />
        <path d="M10 19v-4.5h4V19" />
        <path d="M12 10.25v.01" />
      </svg>
    </span>
  );
}

export function SiteHeader() {
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link href="/" className="site-header__brand">
          <BrandMark />
          <span>Wyszukiwarka szkół</span>
        </Link>
        <nav className="site-nav" aria-label="Główna">
          <NavLinks />
        </nav>
      </div>
    </header>
  );
}
