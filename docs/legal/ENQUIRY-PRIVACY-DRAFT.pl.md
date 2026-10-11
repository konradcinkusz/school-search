# Informacja o przetwarzaniu danych w formularzu zapytań do szkoły (PROJEKT)

> **PROJEKT do weryfikacji przez prawnika. Nie stanowi porady prawnej.**
> Wersja robocza: `projekt-zapytania-2026-10`. Ten dokument dotyczy wyłącznie formularza „Zapytanie do szkoły", który rodzic
> wysyła przez stronę szkoły. Ogólna polityka prywatności Serwisu: `PRIVACY-DRAFT.pl.md`.
> Administrator danych, inspektor ochrony danych, podstawa prawna oraz sposób wycofania zgody są **OPEN** (patrz pkt 8).
> Nie wolno ich uzupełniać bez decyzji właściciela i prawnika.

## 1. Co to jest formularz

Rodzic może wysłać wiadomość do właściciela szkoły, który zgłosił uprawnienie i wykupił płatny profil. Wiadomość trafia wyłącznie
do panelu właściciela w Serwisie. **Serwis nie wysyła wiadomości e-mail** w tej wersji i nie przekazuje jej nikomu poza
właścicielem szkoły.

## 2. Jakie dane zbieramy

| Dane | Skąd | Obowiązkowe | Gdzie są przechowywane | Okres |
|---|---|---|---|---|
| Adres e-mail rodzica | Formularz | Tak | Baza usługi płatnych profili (`billingdb`) | 90 dni od wysłania |
| Treść wiadomości (do 1000 znaków) | Formularz | Tak | Ta sama baza | 90 dni od wysłania |
| Zgoda: wersja tekstu zgody i moment jej wyrażenia | Formularz | Tak | Ta sama baza | 90 dni od wysłania |
| Status „nowe / przeczytane" | System | Tak | Ta sama baza | 90 dni od wysłania |
| Pole pułapka (ukryte, do wykrywania botów) | Formularz | Nie | Nie zapisywane. Wysłanie wypełnionego pola kończy się bez zapisu | Nie dotyczy |

Nie zbieramy imienia, nazwiska, numeru telefonu, adresu zamieszkania ani danych dziecka. Prosimy, aby w treści wiadomości
nie podawać danych dziecka ani innych osób. Jeżeli je podasz, pozostają w bazie przez ten sam okres i są widoczne tylko dla
właściciela szkoły.

## 3. Cele i zasady

1. Cel: przekazanie wiadomości właścicielowi szkoły, który może na nią odpowiedzieć.
2. Dane **nie są używane do marketingu**, do profilowania ani do działań reklamowych.
3. Dane **nie są publiczne**. Nie są widoczne na stronie szkoły, w wynikach wyszukiwania, w zestawieniach ani w narzędziach
   dla systemów AI.
4. Adres e-mail rodzica jest widoczny wyłącznie dla właściciela szkoły, który zgłosił uprawnienie.

## 4. Zgoda

1. Wysłanie formularza wymaga zaznaczenia pola zgody. Pole nie jest zaznaczone domyślnie.
2. Treść zgody jest wersjonowana. Zapisujemy wersję tekstu, który zobaczyłeś, oraz moment wyrażenia zgody.
3. Zgodę można wycofać: **OPEN** (sposób wycofania, np. adres kontaktowy lub przycisk. Do czasu decyzji właściciela i prawnika
   nie podajemy tu żadnego kanału).
4. Treść zgody (wersja `DRAFT`) jest zapisana w kodzie Serwisu i podawana przez endpoint `/api/v1/legal/enquiry-consent`.
   Wymaga zatwierdzenia przez prawnika przed uruchomieniem sprzedaży.

## 5. Odbiorcy

1. Właściciel szkoły, który zgłosił uprawnienie (w panelu).
2. Dostawca hostingu platformy (Fly.io, region `waw`). **OPEN:** umowa powierzenia przetwarzania i jej warunki.
3. Dostawca płatności nie otrzymuje treści zapytań. **OPEN:** potwierdzenie tego po wyborze dostawcy (ADR-0016).

## 6. Okres przechowywania

1. Każde zapytanie wygasa 90 dni po wysłaniu. Zadanie działające w tle usuwa wygasłe zapytania.
2. Właściciel może usunąć zapytanie wcześniej w panelu.
3. Kopie zapasowe bazy: **OPEN** (okres przechowywania kopii i ich usuwanie).
4. Logi: treść wiadomości i adres e-mail nie są zapisywane w logach aplikacji. Okres przechowywania logów platformy: **OPEN**.

## 7. Twoje prawa

Masz prawo dostępu do swoich danych, ich sprostowania, usunięcia, ograniczenia przetwarzania, sprzeciwu oraz skargi do
organu nadzorczego. **OPEN:** organ nadzorczy, sposób składania żądań i termin ich rozpatrzenia. Składanie żądań wymaga
podania kontaktu, który nie jest jeszcze ustalony.

## 8. Lista punktów OPEN

| Punkt | Pkt | Kto rozstrzyga |
|---|---|---|
| Administrator danych, dane kontaktowe, inspektor ochrony danych | Wstęp, 7 | Właściciel i prawnik |
| Podstawa prawna przetwarzania (propozycja: zgoda) | 4 | Prawnik |
| Sposób wycofania zgody | 4.3 | Właściciel i prawnik |
| Umowa powierzenia z dostawcą hostingu | 5.2 | Właściciel i prawnik |
| Potwierdzenie braku przekazania danych dostawcy płatności | 5.3 | Właściciel (po wyborze dostawcy) |
| Okres przechowywania kopii zapasowych i logów | 6.3, 6.4 | Właściciel |
| Sposób składania żądań i organ nadzorczy | 7 | Prawnik |
