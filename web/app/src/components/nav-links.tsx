"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/**
 * Main navigation links. The current section is marked with aria-current="page" so it is announced and styled;
 * nothing else changes with the route.
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
  return (
    <ul>
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
