import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Metodologia",
  description:
    "Jak liczymy wyniki szkół: wynik z poziomem pewności, wskaźnik kontrolny pokrycia kohorty, znane ograniczenia. Nie jest oficjalnym rankingiem.",
};

const METHODOLOGY_VERSION = "0.0-draft";

export default function MethodologyPage() {
  return (
    <article>
      <h1>Metodologia</h1>
      <div className="notice" role="note">
        Wersja metodologii: <strong>{METHODOLOGY_VERSION}</strong> (szkic). Ten dokument zmienia się wraz
        z wersją i każda zmiana jest opisana. Nie jest oficjalnym rankingiem.
      </div>

      <h2>Czym jest ta strona</h2>
      <p>
        To niezależne narzędzie, które zestawia publicznie dostępne dane o szkołach podstawowych.
        Nie jest oficjalnym rankingiem, nie jest zaleceniem ani opinią organu prowadzącego, kuratorium
        ani ministerstwa. Pomaga zrozumieć dane, a nie zastępuje rozmowy z nauczycielami i wizyty
        w szkole.
      </p>

      <h2>Wynik i poziom pewności</h2>
      <p>
        Każdy wynik szkoły występuje zawsze razem z poziomem pewności. Nigdy nie pokazujemy samej
        liczby bez informacji o tym, na czym się opiera. Każdy wynik zawiera:
      </p>
      <ul>
        <li>wartość wyniku, czyli wskaźnik wartości dodanej opisany poniżej,</li>
        <li>liczebność kohorty, czyli liczbę uczniów, na której wynik został obliczony,</li>
        <li>przedział niepewności,</li>
        <li>wersję metodologii, według której wynik został policzony,</li>
        <li>datę danych (stan na dzień) oraz źródło.</li>
      </ul>
      <p>
        Jeżeli kohorta jest zbyt mała, wynik nie jest pokazywany. Zamiast liczby pojawia się
        informacja o zbyt małej kohorcie. Brak wyniku nie oznacza zera ani gorszej oceny. Próg
        liczebności jest zapisany w jednym miejscu, a jego uzasadnienie zostanie opublikowane razem
        z wersją metodologii.
      </p>

      <h2>Składowe wyniku</h2>
      <p>
        Składowa egzaminacyjna porównuje wyniki szkoły z średnią krajową dla danego przedmiotu i roku
        (standaryzacja). Obejmuje trzy przedmioty z równymi wagami: język polski, matematykę oraz język
        obcy nowożytny.
      </p>

      <h2>Wskaźnik kontrolny: pokrycie kohorty</h2>
      <p>
        Sam wynik egzaminu może być zawyżony, jeśli szkoła przystępuje do egzaminu tylko część uczniów
        albo jeśli zmienia się skład przystępujących. Dlatego wynik uwzględnia wskaźnik pokrycia
        kohorty, czyli udział uczniów rocznika, którzy przystąpili do egzaminu. Wskaźnik jest wplatany
        w składową egzaminacyjną, a nie pokazywany obok jako osobna odznaka. Gdy pokrycie spada, wynik
        powinien spaść, nawet jeśli średnia rośnie.
      </p>

      <h2>Znane ograniczenia i obciążenia</h2>
      <ul>
        <li>
          <strong>Wartość dodana, a nie jakość nauczania.</strong> Wynik nie mierzy wszystkiego, co
          szkoła daje uczniom: wychowania, zajęć pozalekcyjnych, atmosfery ani wsparcia dla dzieci
          ze specjalnymi potrzebami.
        </li>
        <li>
          <strong>Skład uczniów.</strong> Szkoły o innym składzie społecznym i ekonomicznym uczniów nie
          są w pełni porównywalne. Nie korygujemy tego w wersji {METHODOLOGY_VERSION}.
        </li>
        <li>
          <strong>Selekcja do egzaminu.</strong> Nawet z wskaźnikiem pokrycia część zniekształceń
          pozostaje, zwłaszcza w małych kohortach.
        </li>
        <li>
          <strong>Jeden rok danych.</strong> Pojedynczy rok bywa przypadkowy. Wyniki należy czytać
          razem z poziomem pewności i kohortą.
        </li>
        <li>
          <strong>Obwód a wybór szkoły.</strong> Obwód szkolny nie zawsze odpowiada temu, co
          rodzic faktycznie wybiera. Wskazanie szkoły w zasięgu nie jest rekomendacją.
        </li>
        <li>
          <strong>Dane i ich jakość.</strong> Dane pochodzą od instytucji zewnętrznych i mogą zawierać
          błędy. Każda wartość ma źródło, a szkoła może zgłosić korektę.
        </li>
      </ul>

      <h2>Czego nie robimy</h2>
      <ul>
        <li>Nie publikujemy nazwisk uczniów ani danych umożliwiających ich identyfikację.</li>
        <li>Nie oceniamy na podstawie pochodzenia ani narodowości uczniów.</li>
        <li>Nie przyjmujemy opłat za pozycję w wynikach. Wynik nie jest do kupienia.</li>
        <li>Nie pobieramy danych z serwisów konkurencyjnych.</li>
      </ul>

      <h2>Korekty i kontakt</h2>
      <p>
        Jeżeli dane dotyczące szkoły są błędne, szkoła lub każdy inny zainteresowany może zgłosić
        korektę na <a href="/dla-szkol">stronie dla szkół</a>. Zgłoszenia są sprawdzane, a zmiany
        opisywane w historii wersji.
      </p>
    </article>
  );
}
