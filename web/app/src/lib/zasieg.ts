export const ZASIEG_VALUES = ["gmina", "powiat", "wojewodztwo"] as const;
export type Zasieg = (typeof ZASIEG_VALUES)[number];

export function isZasieg(value: string): value is Zasieg {
  return (ZASIEG_VALUES as readonly string[]).includes(value);
}

export const ZASIEG_LABEL: Record<Zasieg, string> = {
  gmina: "gmina",
  powiat: "powiat",
  wojewodztwo: "województwo",
};
