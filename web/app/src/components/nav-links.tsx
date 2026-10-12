"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useId, useState } from "react";

/**
 * Main navigation links. The current section is marked with aria-current="page" so it is announced and styled.
 * Below 62rem the list sits behind a "Menu" disclosure button, so no link is ever cut off at the screen edge.
 * Without JavaScript (no `js` class on <html>, set by the inline script in layout.tsx) the button is hidden and the
 * links stay visible as a wrapped row.
 */
const LINKS: ReadonlyArray<{ readonly href: string; readonly label: string; readonly section: string }> = [
  { href: "/ranking/country", label: "Ranking", section: "/ranking" },
  { href: "/przedszkola", label: "Przedszkola", section: "/przedszkola" },
  { href: "/porownaj", label: "Porównaj", section: "/porownaj" },
  { href: "/doradca", label: "Doradca", section: "/doradca" },
  { href: "/metodologia", label: "Metodologia", section: "/metodologia" },
  { href: "/dla-szkol", label: "Dla szkół", section: "/dla-szkol" },
  { href: "/konto", label: "Konto", section: "/konto" },
];

function isCurrent(pathname: string, section: string): boolean {
  return pathname === section || pathname.startsWith(`${section}/`);
}

export function NavLinks() {
  const pathname = usePathname() ?? "/";
  const listId = useId();
  const [open, setOpen] = useState(false);

  // A new page closes the menu.
  useEffect(() => {
    setOpen(false);
  }, [pathname]);

  // Escape closes the open menu and returns focus to its button.
  useEffect(() => {
    if (!open) return;
    function onKey(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      setOpen(false);
      document.getElementById(`${listId}-toggle`)?.focus();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, listId]);

  return (
    <>
      <button
        type="button"
        id={`${listId}-toggle`}
        className="site-nav__toggle"
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen((value) => !value)}
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true" focusable="false">
          {open ? (
            <path d="M6 6l12 12M18 6 6 18" />
          ) : (
            <path d="M4 7h16M4 12h16M4 17h16" />
          )}
        </svg>
        <span>Menu</span>
      </button>
      <ul id={listId} className="site-nav__list" data-open={open ? "true" : "false"}>
        {LINKS.map((link) => (
          <li key={link.href}>
            <Link href={link.href} aria-current={isCurrent(pathname, link.section) ? "page" : undefined}>
              {link.label}
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}
