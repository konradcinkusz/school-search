import Link from "next/link";
import type { Metadata } from "next";
import { AddressForm } from "@/components/address-form";

export const metadata: Metadata = {
  title: "Start",
  description: "Wpisz adres, aby zobaczyć obwód szkolny i szkoły w zasięgu.",
};

export default function HomePage() {
  return (
    <section>
      <h1>Znajdź szkołę podstawową dla swojego adresu</h1>
      <p>
        Wpisz adres, a zobaczysz obwód szkolny oraz szkoły w zasięgu wraz z wynikami, ich
        niepewnością i liczebnością kohort.
      </p>
      <div className="notice" role="note">
        Ta strona jest w przygotowaniu. Wyniki pojawią się w kolejnych fazach wdrożenia.
      </div>
      <AddressForm />
      <p className="muted">
        Każdy wynik jest opisany w <Link href="/metodologia">metodologii</Link>. Nie jest to
        oficjalny ranking.
      </p>
    </section>
  );
}
