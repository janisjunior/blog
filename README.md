# WT - Blog Generator 0.7.4

Program do wybierania galerii JR Wheels, Concaver Wheels i Vesser Forged oraz przygotowywania kompletnych plików do własnego czatu ChatGPT. Jasny panel: wybór marki, lista galerii, podgląd zdjęcia i zapis pliku. Bez importowania odpowiedzi, zatwierdzania artykułów i płatnego API. Może przygotować cotygodniowe paczki w tle i wysłać je przez Twoją pocztę SMTP.

## Jak używać

1. Wybierz **JR Wheels**, **Concaver Wheels** albo **Vesser Forged**. Przy pierwszym wyborze program pobierze listę galerii i sprawdzi archiwum `/blog`.
2. Wyszukaj samochód lub felgi i wybierz galerię. Lista pomija wykryte oraz możliwe dopasowania do wpisów na stronie. Lokalny tekst zapisany w poprzedniej wersji nie blokuje galerii.
3. Kliknij **Otwórz galerię zdjęć**, aby zobaczyć źródło i pobrać wybrane zdjęcia. Podgląd i przycisk **Otwórz zdjęcie** pomagają odnaleźć zdjęcie tej konfiguracji.
4. Wybierz folder i kliknij **Przygotuj plik do ChatGPT**. Program odświeży szczegóły galerii, sprawdzi aktualne `/blog` i oficjalną kartę modelu, a następnie otworzy folder gotowych plików.
5. Wrzuć **Wrzuc do ChatGPT.docx** do swojego czatu. Dokument zawiera pełne polecenie, zweryfikowane dane, wymagania EN/PL, klikalne źródła i zdjęcie JPG/PNG, jeśli zostało pobrane. Nie trzeba kopiować oddzielnego polecenia. Zdjęcie jest też osobnym plikiem w folderze. Jeśli formatem zdjęcia jest WebP, dołącz je osobno. Awaria pobrania zdjęcia nie usuwa dokumentu ani linków do galerii.

Polecenie prosi zawsze najpierw o niezależny artykuł po angielsku (EN), potem po polsku (PL), każdy po 2200–2600 słów. Na samej górze DOCX i TXT znajduje się link do galerii oraz jej źródłowy numer / ID; w DOCX link jest klikalny. Zawiera kompletne wymagania z dostarczonego [dokumentu redakcyjnego](docs/editorial/Wpisy%20na%20bloga.docx). Nie wymyśla danych niepotwierdzonych. Działanie osobistego ChatGPT zależy od wybranego modelu i dostępnych limitów konta; program nie loguje się do niego i nie wywołuje API.

Po przygotowaniu pliku galeria jest oznaczana jako wykorzystana: jej nazwa samochodu i model felg są przekreślone, a wiersz znajduje się na końcu listy. Nadal można otworzyć galerię i przygotować plik ponownie. Przycisk **Oznacz galerię jako wykorzystaną** pozwala oznaczyć także wcześniej użyty temat, a **Cofnij oznaczenie wykorzystania** przywraca go do niewykorzystanych. Oznaczenie pozostaje po restarcie i odświeżeniu listy; nie zmienia historii lokalnych artykułów.

## Sprawdzanie dostępności

Archiwum wszystkich podstron `/blog` jest pobierane raz dla sprawdzanej marki, a następnie porównywane z całą listą. Lista zapamiętuje datę sprawdzenia i jest szybko dostępna przy kolejnych wejściach. **Sprawdź /blog i odśwież listę** aktualizuje ją na żądanie. Nie są pobierane pełne szczegóły kilku tysięcy galerii naraz; sprawdzenie listy używa dostępnych danych i miniatur, a ostateczna kontrola pełnej galerii i zdjęć odbywa się przed eksportem.

Przy przygotowaniu pliku program pobiera galerię i kartę felg tylko raz. Zawsze odczytuje aktualną listę wpisów ze wszystkich podstron `/blog` i natychmiast pobiera nowe wpisy. Treść wcześniej sprawdzonych wpisów przechowuje przez maksymalnie 15 minut, również po ponownym uruchomieniu programu; potem pobiera ją ponownie. Przycisk odświeżenia listy wymusza pełny skan także przed upływem tego czasu. Edycja istniejącego wpisu może więc zostać wykryta dopiero po wygaśnięciu cache albo ręcznym odświeżeniu. Niepełny odczyt aktualnej listy blokuje przygotowanie pliku. Pierwszy skan i ponowne pełne sprawdzenie po upływie 15 minut nadal mogą trwać dłużej.

Pobieranie opcjonalnego zdjęcia ma limit 8 sekund. Jeśli zdjęcie nie zdąży się pobrać, DOCX i TXT zachowują klikalne linki do galerii oraz zdjęć. Przycisk **Anuluj** przerywa przygotowanie na żądanie.

Potwierdzone i możliwe duplikaty oraz nieudane sprawdzenie archiwum blokują zapis pliku. Aktualna karta modelu musi pasować do wybranej konfiguracji. Pierwsze sprawdzanie stron może trwać kilka minut; przycisk **Anuluj** przerywa operację. Program respektuje robots.txt i limity żądań.

## Paczki tygodniowe i poczta

Program przygotowuje wcześniej **3 paczki — jedną JR, jedną Concaver, jedną Vesser**. Są widoczne u góry w sekcji **Paczki na [data]**. Każda zawiera DOCX i TXT z pełnym poleceniem EN → PL, potwierdzonymi danymi, kartą modelu i linkami do zdjęć galerii. W paczkach automatycznych zdjęcia nie są pobierane ani osadzane, żeby mail był mały; ręczne przygotowanie pliku nadal może dołączyć zdjęcie. Paczka nie jest gotowym tekstem artykułu: dokument wrzucasz do własnego ChatGPT.

1. Otwórz **Poczta / harmonogram**.
2. Jeśli masz przygotowany plik JSON, kliknij **Wczytaj gotowe ustawienia z pliku**: serwer, port, TLS, adresy i trzy przełączniki zostaną uzupełnione. Hasło wpisz w PasswordBox i zapisz. Plik nie zawiera hasła, a import odrzuca dodatkowe pola i nie nadpisuje innych ustawień aplikacji. Możesz też podać serwer SMTP dostawcy, port 587 / STARTTLS albo 465 / SSL/TLS, nadawcę, odbiorcę, login i hasło SMTP / hasło aplikacji. Nie zapisuj hasła w repozytorium ani nie podawaj go w czacie. Puste pole hasła zachowuje wcześniej zapisane hasło. Konto musi obsługiwać uwierzytelnienie SMTP dostępne przez login i hasło aplikacji; niektóre konta Microsoft 365 wymagają OAuth lub zmian administracyjnych i nie zadziałają z tym sposobem logowania.
3. Włącz wysyłkę w piątki, zapisz ustawienia i kliknij **Wyślij wiadomość testową**. Sprawdź skrzynkę oraz spam.
4. Zostaw włączone przygotowanie w tle i autostart. Program uruchamia się po zalogowaniu do Windows. Przycisk zamknięcia okna pozostawia go w zasobniku; **Anuluj** wstrzymuje przygotowanie, **Wznów** je odblokowuje.

Termin wysyłki to **piątek 12:00 Europe/Warsaw**, również po zmianie czasu letniego i zimowego. Paczki powstają wcześniej, a przed terminem są odświeżane. W tle ich dostępność i ostatnie modele na blogach są kontrolowane co 6 godzin. W momencie wysyłki program ponownie sprawdza aktualne listy blogów i ostatnie wpisy. Wolne źródła lub niekompletny zestaw mogą opóźnić mail; program nie wysyła pozornego potwierdzenia ukończenia ani dwóch paczek zamiast trzech. Wybór pomija wykorzystane galerie, ostatni model z bloga danej marki i model z jej poprzedniej paczki. Jeśli nie ma odpowiedniego innego modelu, status wyjaśnia brak zestawu.

Komputer musi być włączony, użytkownik zalogowany i internet dostępny. Pominięty termin zostaje nadrobiony po uruchomieniu; aplikacja na komputerze nie wysyła maili, gdy komputer jest wyłączony. Windows ma również zadanie kontrolne co 15 minut i po zalogowaniu; właściwy termin i strefę wyznacza program. Aplikacja sprawdza termin co 30 sekund. Błędy przygotowania są ponawiane po 15 minutach albo po użyciu **Wznów**.

**Wyślij 3 paczki teraz** pozwala wysłać przygotowany zestaw od razu na zapisany adres, także przy wyłączonej wysyłce automatycznej. Przycisk jest aktywny po przygotowaniu wszystkich trzech paczek i zakończeniu innych operacji. Przed wysłaniem program sprawdza blogi. Postęp pokazuje markę, numer wpisu i aktualny adres. Gdy 15-minutowa pamięć treści wygasła, pełna kontrola może trwać kilka minut. Kontrola ma łączny limit 10 minut; po jego przekroczeniu wiadomość nie jest wysyłana, a komunikat wskazuje ostatni etap. Po poprawnym sprawdzeniu pojawia się oddzielny etap SMTP. Zestaw wysłany ręcznie nie zostaje wysłany ponownie w piątek.

Mail zawiera klikalne linki do każdej galerii i do nowego czatu na https://chatgpt.com/, jeden ZIP z trzema folderami oraz link `wtblog://weekly/[data]` do otwarcia zainstalowanej aplikacji. Część klientów pocztowych nie obsługuje takich linków; wtedy użyj załącznika lub otwórz program ze skrótu. Program zapisuje stan wysyłki. Utrata odpowiedzi SMTP nie powoduje automatycznej ponownej wysyłki: sprawdź skrzynkę i w ustawieniach poczty potwierdź odbiór albo świadomie zleć ponowienie.

## Pobieranie i aktualizacja

Na GitHubie otwórz najnowsze udane uruchomienie workflow Windows i pobierz **Artifacts → WT-Blog-Generator-Windows**. Rozpakuj ZIP i uruchom **WT-Blog-Generator-Setup.exe**. Przed aktualizacją zakończ starszą wersję programu i jej zadania w tle; po instalacji uruchom komputer ponownie.

Przed udostępnieniem nowej paczki workflow aktualizuje sygnatury Microsoft Defender i skanuje aplikację, instalator oraz ZIP. Niedostępny skaner, błąd skanu lub wykrycie zagrożenia blokują udostępnienie paczki. `Security-report.txt` zawiera wynik skanu i stan podpisów cyfrowych, a `SHA256SUMS.txt` sumy kontrolne instalatora i przenośnego ZIP. Skan nie gwarantuje bezpieczeństwa ani braku ostrzeżeń Chrome; pliki wykonywalne obecnie nie mają podpisu cyfrowego. Jeżeli przeglądarka lub antywirus blokuje pobranie jako niebezpieczne, nie omijaj blokady i nie wyłączaj ochrony.

Dane starszej wersji w `%LOCALAPPDATA%\WheelContentManager` pozostają zachowane. Nowy interfejs nie wykorzystuje statusu lokalnego artykułu do blokowania galerii. Stare zadania generowania przez API są wyłączane. Nowy autostart uruchamia program w zasobniku Windows, a nowy Worker przygotowuje wyłącznie paczki z poleceniami. Zamknięcie okna chowa program do zasobnika. Menu ikony pozwala otworzyć program, wznowić przygotowanie albo wstrzymać je i zakończyć aplikację. Zapisane sekrety poczty pozostają zaszyfrowane przez DPAPI dla tego samego konta Windows.

## Rozwój i sprawdzenie

Aplikacja jest natywnym WPF na Windows. SDK: .NET 10.0.401. W chmurze użyj istniejącego `/workspace/blog`, bez tworzenia dodatkowego worktree. `bash scripts/setup-cloud.sh` weryfikuje SDK, przywraca zależności z lockfile, buduje rozwiązanie i uruchamia testy. Linux nie uruchamia WPF; workflow Windows buduje instalator i sprawdza Worker.

Stare modele, migracje i testy pozostają w bibliotekach dla zgodności danych. Szczegóły źródeł i weryfikacji: [ZRODLA.md](docs/ZRODLA.md), [WERYFIKACJA.md](docs/WERYFIKACJA.md). Interaktywne działanie Windows i wrzucenie dokumentu do osobistego ChatGPT wymagają ręcznej weryfikacji.

## Błędy bloga i TLS

Jeżeli link wpisu zwraca listę bloga zamiast artykułu, program pokazuje dokładny adres i nie uznaje archiwum za kompletne. Problem jednej marki nie zatrzymuje przygotowania pozostałych; mail z trzema paczkami czeka na pełne sprawdzenie. Uszkodzony link trzeba poprawić na stronie źródłowej.

Przy błędzie TLS poczty program pokazuje wynik weryfikacji, nazwę DNS z certyfikatu, wystawcę, okres ważności i problemy łańcucha. Przycisk **Skopiuj komunikat** kopiuje te publiczne dane, bez hasła. Weryfikacja certyfikatu pozostaje wymagana. Błąd certyfikatu występuje przed logowaniem; użyj poprawnej nazwy serwera od dostawcy i prawidłowego certyfikatu zamiast wyłączania ochrony.
