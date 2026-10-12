"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef } from "react";

/**
 * Main navigation links. The current section is marked with aria-current="page" so it is announced and styled.
 * On a phone the list is one horizontally scrollable row; the current link is scrolled into that row's view
 * (the row only, never the page).
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
  const listRef = useRef<HTMLUListElement>(null);

  useEffect(() => {
    const list = listRef.current;
    if (list === null || list.scrollWidth <= list.clientWidth) return;
    const current = list.querySelector<HTMLElement>('[aria-current="page"]');
    if (current === null) {
      list.scrollLeft = 0;
      return;
    }
    const left = current.offsetLeft - list.offsetLeft;
    const right = left + current.offsetWidth;
    if (left < list.scrollLeft || right > list.scrollLeft + list.clientWidth) {
      list.scrollLeft = Math.max(0, left - 16);
    }
  }, [pathname]);

  return (
    <ul ref={listRef}>
      {LINKS.map((link) => (
        <li key={link.href}>
          <Link href={link.href} aria-current={isCurrent(pathname, link.section) ? "page" : undefined}>
            {link.label}
          </Link>
        </li>
      ))}
    </ul>
  );
}
