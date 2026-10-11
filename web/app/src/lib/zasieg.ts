import { isTeryt } from "@/lib/validation";

/** Ranking scope in the URL: "country" or "gmina:<TERYT>" (for example gmina:1465011). */
export type RankingScope =
  | { readonly kind: "country"; readonly param: "country" }
  | { readonly kind: "gmina"; readonly param: string; readonly gminaTeryt: string };

export function parseRankingScope(raw: string): RankingScope | null {
  if (raw === "country") return { kind: "country", param: "country" };
  const match = /^gmina:(.+)$/.exec(raw);
  if (!match || match[1] === undefined) return null;
  const teryt = match[1];
  if (!isTeryt(teryt)) return null;
  return { kind: "gmina", param: `gmina:${teryt}`, gminaTeryt: teryt };
}

export function rankingHref(scope: RankingScope): string {
  return `/ranking/${scope.param}`;
}
