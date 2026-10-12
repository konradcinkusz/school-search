"use client";

import { useEffect } from "react";

/**
 * Moves focus to the results heading or count after a GET search, so a screen reader and a keyboard user
 * land on the new results instead of on <body>. Mount it only when a search was submitted; the parent keys it
 * by the query string so a new search or page remounts it. A target without tabindex is made programmatically
 * focusable (-1), which keeps it out of the tab order.
 */
export function FocusOnResults({ targetId }: { targetId: string }) {
  useEffect(() => {
    const target = document.getElementById(targetId);
    if (!target) return;
    if (!target.hasAttribute("tabindex")) target.setAttribute("tabindex", "-1");
    target.focus();
  }, [targetId]);
  return null;
}
