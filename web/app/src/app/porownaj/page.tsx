import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Porównaj szkoły",
  description: "Porównanie szkół obok siebie z wynikami, pewnością i metodologią.",
};

export default function ComparePage() {
  return (
    <section>
      <h1>Porównaj szkoły</h1>
      <p>
        Tu porównasz wybrane szkoły obok siebie: wyniki, poziom pewności, liczebność kohorty
        i wersję metodologii. Każda pozycja ma źródło i datę.
      </p>
      <div className="notice" role="note">
        Porównywarka jest w przygotowaniu.
      </div>
    </section>
  );
}
