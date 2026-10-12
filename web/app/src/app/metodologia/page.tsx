import type { Metadata } from "next";
import { catalog, type Methodology } from "@/lib/api";
import { formatPercent } from "@/lib/format";
import { MethodologyQa } from "@/components/ai/methodology-qa";
import { NotSoldList } from "@/components/billing/not-sold-list";
import "../styles/static.css";

// Live values come from /api/v1/methodology per request; the fallback is the documented 1.0.0 set.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Metodologia",
  description:
    "Jak liczymy wyniki szkół: wynik z przedziałem, wskaźnik udziału w egzaminie, próg liczebności i znane ograniczenia. Nie jest oficjalnym rankingiem.",
  alternates: { canonical: "/metodologia" },
};

/** Fallback when the API cannot be reached. Matches docs/methodology/RANKING-METHODOLOGY.md (1.0.0). */
const FALLBACK: Methodology = {
  version: "1.0.0",
  publicationThreshold: 9,
  coverageFloor: 0.8,
  weights: "equal",
  intervalZ: 1.96,
  description: "",
  docsUrl: "",
};

function intervalZLabel(value: number): string {
  return value.toLocaleString("pl-PL", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export default async function MethodologyPage() {
  const result = await catalog.methodology();
  const live = result.ok ? result.data : null;
  const m = live ?? FALLBACK;
  const version = m.version;

  return (
    <article className="prose">
      <header className="page-header">
        <h1>Metodologia</h1>
        <p className="lead">Jak liczymy i pokazujemy wyniki szkół, czego nie mierzą i jakie mają ograniczenia.</p>
      </header>
      <div className="notice notice--caution" role="note">
        Wersja metodologii: <strong>{version}</strong>. Ten dokument zmienia się wraz z wersją i każda zmiana jest
        opisana. Nie jest oficjalnym rankingiem.
        {live ? null : (
          <>
            {" "}
            Nie udało się pobrać bieżących wartości z usługi, dlatego pokazujemy wartości z opisu wersji {FALLBACK.version}.
          </>
        )}
      </div>

      <div className="measure-block">
        <nav className="toc" aria-label="Spis treści">
          <p>Na tej stronie</p>
          <ul>
            <li>
              <a href="#czym-jest">Czym jest ta strona</a>
            </li>
            <li>
              <a href="#wynik">Wynik i poziom pewności</a>
            </li>
            <li>
              <a href="#skladowe">Składowe wyniku</a>
            </li>
            <li>
              <a href="#pokrycie">Wskaźnik kontrolny: udział w egzaminie</a>
            </li>
            <li>
              <a href="#ograniczenia">Znane ograniczenia i obciążenia</a>
            </li>
            <li>
              <a href="#czego-nie-robimy">Czego nie robimy</a>
            </li>
            <li>
              <a href="#korekty">Korekty i kontakt</a>
            </li>
            <li>
              <a href="#pytania">Zapytaj o metodologię</a>
            </li>
          </ul>
        </nav>
      </div>

      <h2 id="czym-jest">Czym jest ta strona</h2>
      <p>
        To niezależne narzędzie, które zestawia publicznie dostępne dane o szkołach podstawowych. Nie jest oficjalnym
        rankingiem, nie jest zaleceniem ani opinią organu prowadzącego, kuratorium ani ministerstwa. Pomaga zrozumieć
        dane, a nie zastępuje rozmowy z nauczycielami i wizyty w szkole.
      </p>

      <h2 id="wynik">Wynik i poziom pewności</h2>
      <p>Każdy wynik szkoły występuje zawsze razem z poziomem pewności. Każdy wynik zawiera:</p>
      <ul>
        <li>wartość wyniku, czyli wskaźnik wartości dodanej opisany poniżej,</li>
        <li>grupa porównawcza (kohorta), czyli liczba uczniów, na której wynik został obliczony,</li>
        <li>przedział niepewności 95%,</li>
        <li>wersję metodologii, według której wynik został policzony,</li>
        <li>datę danych (stan na dzień) oraz źródło.</li>
      </ul>
      <p>
        Wynik publikujemy od <strong>{m.publicationThreshold}</strong> uczniów w grupie porównawczej. Poniżej tego progu
        wyniku nie pokazujemy: zamiast liczby pojawia się informacja o zbyt małej grupie porównawczej. Brak wyniku nie oznacza zera ani gorszej
        oceny. Próg liczebności jest zapisany w jednym miejscu, a jego uzasadnienie jest opublikowane razem z wersją
        metodologii.
      </p>

      <h2 id="skladowe">Składowe wyniku</h2>
      <p>
        Składowa egzaminacyjna porównuje wyniki szkoły z średnią krajową dla danego przedmiotu i roku (standaryzacja).
        Obejmuje trzy przedmioty z równymi wagami: język polski, matematykę oraz język obcy nowożytny. Przedział
        niepewności 95% liczymy ze współczynnikiem z = <strong>{intervalZLabel(m.intervalZ)}</strong>.
      </p>

      <h2 id="pokrycie">Wskaźnik kontrolny: udział w egzaminie</h2>
      <p>
        Wynik egzaminu może być zawyżony, jeśli szkoła przystępuje do egzaminu tylko część uczniów albo zmienia się
        skład przystępujących. Dlatego wynik uwzględnia udział uczniów rocznika, którzy przystąpili do egzaminu. Wskaźnik
        jest wplatany w składową egzaminacyjną, a nie pokazywany obok jako osobna odznaka. Poniżej udziału{" "}
        <strong>{formatPercent(m.coverageFloor * 100)}</strong> wynik jest proporcjonalnie obniżany, nawet jeśli średnia
        rośnie.
      </p>

      <h2 id="ograniczenia">Znane ograniczenia i obciążenia</h2>
      <ul>
        <li>
          <strong>Wartość dodana, a nie jakość nauczania.</strong> Wynik nie mierzy wszystkiego, co szkoła daje uczniom:
          wychowania, zajęć pozalekcyjnych, atmosfery ani wsparcia dla dzieci ze specjalnymi potrzebami.
        </li>
        <li>
          <strong>Skład uczniów.</strong> Szkoły o innym składzie społecznym i ekonomicznym uczniów nie są w pełni
          porównywalne. Nie korygujemy tego w wersji {version}.
        </li>
        <li>
          <strong>Selekcja do egzaminu.</strong> Nawet z wskaźnikiem pokrycia część zniekształceń pozostaje, zwłaszcza w
          małych grupach porównawczych.
        </li>
        <li>
          <strong>Jeden rok danych.</strong> Pojedynczy rok bywa przypadkowy. Wyniki czytamy razem z poziomem pewności i
          liczbą uczniów w grupie porównawczej.
        </li>
        <li>
          <strong>Obwód a wybór szkoły.</strong> Obwód szkolny nie zawsze odpowiada temu, co rodzic faktycznie wybiera.
          Wskazanie szkoły w zasięgu nie jest rekomendacją.
        </li>
        <li>
          <strong>Dane i ich jakość.</strong> Dane pochodzą od instytucji zewnętrznych i mogą zawierać błędy. Każda
          wartość ma źródło, a szkoła może zgłosić korektę.
        </li>
      </ul>

      <h2 id="czego-nie-robimy">Czego nie robimy</h2>
      <NotSoldList
        items={[
          "Nie publikujemy nazwisk uczniów ani danych umożliwiających ich identyfikację.",
          "Nie oceniamy na podstawie pochodzenia ani narodowości uczniów.",
          "Nie przyjmujemy opłat za pozycję w wynikach. Wynik nie jest do kupienia.",
          "Nie pobieramy danych z serwisów konkurencyjnych.",
        ]}
      />

      <h2 id="korekty">Korekty i kontakt</h2>
      <p>
        Jeżeli dane dotyczące szkoły są błędne, szkoła lub każdy inny zainteresowany może zgłosić korektę na{" "}
        <a href="/dla-szkol">stronie dla szkół</a>. Zgłoszenia są sprawdzane, a zmiany opisywane w historii wersji.
      </p>

      <div id="pytania" className="measure-block">
        <MethodologyQa />
      </div>
    </article>
  );
}
