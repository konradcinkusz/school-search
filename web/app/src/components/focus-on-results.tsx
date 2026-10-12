"use client";

import { useEffect } from "react";

/**
 * Moves focus to the results heading or count after a GET search, so a screen reader and a keyboard user
 * land on the new results instead of on <body>. Mount it only when a search was submitted; the parent keys it
 * by the query string so a new search or page remounts it.
 */
export function FocusOnResults({ targetId }: { targetId: string }) {
  useEffect(() => {
    document.getElementById(targetId)?.focus();
  }, [targetId]);
  return null;
}
