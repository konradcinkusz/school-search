# Polityka prywatności (PROJEKT)

> **PROJEKT do weryfikacji przez prawnika. Nie stanowi porady prawnej.**
> Administrator danych, dane kontaktowe, okresy przechowywania i podstawy prawne oznaczone jako OPEN nie są jeszcze
> ustalone. Nie należy ich uzupełniać bez decyzji właściciela i prawnika.

## 1. Administrator

**OPEN:** nazwa, adres i dane kontaktowe administratora. Inspektor ochrony danych: **OPEN**.

## 2. Jakie dane przetwarzamy

| Dane | Skąd | Gdzie są przechowywane | Uwagi |
|---|---|---|---|
| Adres e-mail i hasło konta | Formularz rejestracji | Usługa uwierzytelniania (authservice). **Serwis szkoły-search ich nie przechowuje** | Hasło nigdy nie trafia do bazy serwisu opinii |
| Identyfikator konta (`sub`) | Token dostępu | Tylko jako skrót SHA-256 z sekretem serwera (pepper). Nie ma sposobu odwrócenia bez sekretu | Pseudonimizacja: dla administratora nadal jest to dana osobowa |
| Ocena, tytuł, treść, rok szkolny opinii | Formularz opinii | Baza serwisu opinii | Treść może przypadkowo zawierać dane osób trzecich; moderacja je usuwa |
| Miesiąc publikacji | System | Baza serwisu opinii | Publicznie widoczny tylko miesiąc i rok |
| Dane zgłaszającego (opcjonalnie: imię, e-mail) | Formularz zgłoszenia | Baza serwisu opinii | Podawane dobrowolnie; e-mail służy do przekazania decyzji |
| Treść zgłoszenia i uzasadnienie decyzji | Formularz, moderator | Baza serwisu opinii | Zgłoszenie anonimowe jest dozwolone |
| Dane techniczne (adres IP w limitach zapytań) | Żądanie HTTP | Pamięć procesu | Limity są liczone w pamięci i ulegają zerowaniu po restarcie. Okres przechowywania logów platformy Fly.io: **OPEN** |

Serwis **nie publikuje** adresu e-mail, imienia ani identyfikatora autora. Publicznie widoczna jest etykieta
„Rodzic/opiekun (niezweryfikowany)" oraz miesiąc publikacji.

## 3. Cele i podstawy prawne (propozycje do oceny przez prawnika)

| Cel | Propozycja podstawy (RODO) | Uwagi |
|---|---|---|
| Konto i obsługa opinii | art. 6 ust. 1 lit. b (umowa) | |
| Moderacja i spełnienie obowiązków z DSA (zgłoszenia, uzasadnienia, odwołania) | art. 6 ust. 1 lit. c (obowiązek prawny) lub lit. f (prawnie uzasadniony interes) | Lit. f wymaga testu równowagi |
| Limity zapytań i bezpieczeństwo | art. 6 ust. 1 lit. f | |
| Treści dotyczące zdrowia lub innych danych szczególnych kategorii w opiniach | art. 9 — **OPEN** | Moderacja usuwa takie treści; podstawa wyjątku wymaga oceny |

## 4. Okresy przechowywania

**OPEN.** Okresy dla opinii, zgłoszeń, decyzji i odwołań ustali właściciel i prawnik. Dane kont przechowuje
usługa uwierzytelniania według jej własnej polityki (**OPEN**).

## 5. Odbiorcy i podmioty przetwarzające

| Podmiot | Rola | Uwagi |
|---|---|---|
| Fly.io | Hosting aplikacji i bazy danych | Region `waw` (Polska) |
| Usługa uwierzytelniania (authservice, hostowana przez właściciela na Fly.io) | Konta i tokeny | |
| Anthropic | Pomocnicza klasyfikacja treści opinii (tylko przy włączonej funkcji) | Wysyłany jest wyłącznie tytuł i treść opinii, bez skrótu autora i bez nazwy szkoły. Funkcja jest domyślnie wyłączona. **OPEN: podstawa przekazania i lokalizacja przetwarzania** |

## 6. Prawa osób, których dane dotyczą

Prawa z art. 15–22 RODO (dostęp, sprostowanie, usunięcie, ograniczenie, sprzeciw, przenoszenie). Kontakt w sprawie praw:
**OPEN**. Skarga do organu nadzorczego: Prezes Urzędu Ochrony Danych Osobowych (ustawowo właściwy organ; dane
kontaktowe do uzupełnienia przez prawnika).

Konto można usunąć przez usługę uwierzytelniania; sposób dla użytkownika w interfejsie jest **OPEN**. Usunięcie konta nie usuwa automatycznie opinii
z bazy serwisu opinii: sposób łączenia obu zdarzeń jest **OPEN**.

## 7. Bezpieczeństwo

- Hasła są obsługiwane wyłącznie przez usługę uwierzytelniania.
- Sesja przeglądarki jest zaszyfrowana po stronie serwera (AES-256-GCM), w ciasteczku `httpOnly`. Token dostępu nie trafia
  do przeglądarki.
- Połączenia z usługami odbywają się przez HTTPS.
- Dostęp moderatora wymaga roli nadanej w usłudze uwierzytelniania i jest sprawdzany po stronie serwera.

## 8. Zmiany

Zmiany polityki są publikowane z datą. **OPEN:** sposób informowania użytkowników.
