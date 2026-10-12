import Link from "next/link";
import { NavLinks } from "@/components/nav-links";

/**
 * Decorative brand mark: a school roof over an open book, with a small marker dot (the address the visitor starts
 * from). Hidden from assistive technology; the link text names the site.
 */
export function BrandMark({ className = "brand-mark" }: { readonly className?: string }) {
  return (
    <span className={className} aria-hidden="true">
      <svg viewBox="0 0 32 32" fill="none" focusable="false">
        <path d="M6 13.5 16 7l10 6.5" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
        <path
          d="M8.5 24.5c2.6-1.3 5.1-1.3 7.5 0 2.4-1.3 4.9-1.3 7.5 0v-8.2c-2.6-1.3-5.1-1.3-7.5 0-2.4-1.3-4.9-1.3-7.5 0z"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinejoin="round"
        />
        <path d="M16 16.3v8.2" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        <circle className="brand-mark__dot" cx="25.5" cy="7" r="2.6" />
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
          <span className="site-header__name">Wyszukiwarka szkół</span>
        </Link>
        <nav className="site-nav" aria-label="Główna">
          <NavLinks />
        </nav>
      </div>
    </header>
  );
}
