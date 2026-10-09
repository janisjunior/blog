# WT - Blog Generator

Aplikacja desktopowa dla Windows 11, przygotowująca artykuły o konfiguracjach samochodów na felgach JR Wheels, Concaver Wheels i Vesser Forged. Interfejs jest po polsku, a treści PL i EN powstają w osobnych zadaniach AI. Aplikacja nie publikuje artykułów na stronach.

## Jak uruchomić — bez Visual Studio

1. Otwórz [zakładkę Actions repozytorium](https://github.com/janisjunior/blog/actions).
2. Wybierz zakończone zielonym znacznikiem uruchomienie **Windows — kompilacja, testy i paczka EXE**.
3. Na dole, w **Artifacts**, pobierz **WT-Blog-Generator-Windows** (GitHub wymaga zalogowania do pobrania).
4. Rozpakuj pobrany ZIP. Uruchom `WT-Blog-Generator-Setup.exe`, jeśli instalator jest w paczce. Alternatywnie rozpakuj wewnętrzny `WT-Blog-Generator-win-x64.zip` do stałego folderu i uruchom `WheelContentManager.Desktop.exe`.
5. Zachowaj wszystkie pliki paczki razem, w tym podfolder `Worker`. Wersja samodzielna zawiera .NET — nie trzeba instalować SDK.

Paczka jest niepodpisana cyfrowo. Windows może wyświetlić informację o nieznanym wydawcy. Nazwa repozytorium pozostaje `blog`; nazwa programu to WT - Blog Generator.

## Autostart, anulowanie i szybsze pisanie — 0.4.0

Program uruchamia się po zalogowaniu do Windows jako ikona w zasobniku przy zegarze. Dwuklik otwiera panel; zamknięcie okna pozostawia aplikację w tle. Po ukończeniu zestawu trzech artykułów aplikacja wyświetla powiadomienie systemowe (oprócz skonfigurowanej wysyłki SMTP). Autostart panelu można wyłączyć w Ustawieniach. Działa na tym samym zalogowanym koncie, z zachowaniem DPAPI i danych.

Dolny pasek pokazuje aktualny etap także z osobnego Workera. **Anuluj** przerywa bieżące żądania i wstrzymuje automatyczne przygotowanie, również po restarcie, do wybrania **Wznów przygotowanie w tle** na Pulpicie. Gotowe teksty i ukończone wersje językowe pozostają w bazie. Menu ikony pozwala anulować albo zakończyć panel i wstrzymać przygotowanie. Konflikt z działającym Workerem pokazuje status i możliwość anulowania, zamiast okna błędu.

PL i EN powstają równolegle jako niezależne artykuły, z osobnymi kontrolami. Wspólny budżet obejmuje jednoczesne żądania i zachowuje rezerwy kosztu przy anulowaniu po wysłaniu zapytania. Wznawianie pomija ukończone wersje i ponownie używa prawidłowej analizy zdjęć tej galerii. Analiza i kontrola mają mniejsze limity odpowiedzi; długość artykułów pozostaje 2200–2600 słów. Automatyczny cykl pobiera szczegóły wybranego tematu, bez dodatkowego wczytywania trzech innych galerii każdej marki. Sprawdzanie wszystkich podstron /blog i aktualnej karty modelu pozostaje obowiązkowe; pierwszy skan archiwum oraz czas odpowiedzi dostawcy AI mogą nadal trwać.

## Gotowy zestaw przed otwarciem — wersja 0.3.0

Po skonfigurowaniu AI i zapisaniu ustawień program automatycznie rejestruje zadanie przygotowania w tle i uruchamia pierwszą próbę. Okno można zamknąć. Kolejne sprawdzenia następują po zalogowaniu do Windows oraz co 2 godziny, od 06:00. Komputer musi być włączony, konto zalogowane, a dostęp do internetu i AI skonfigurowany. Na wyłączonym komputerze artykuły nie powstaną; pierwszy zestaw trzeba przygotować przed możliwością jego odczytu.

Pulpit pokazuje **3 gotowe artykuły**, po jednym PL + EN dla JR Wheels, Concaver Wheels i Vesser Forged. Gotowe i zatwierdzone teksty pozostają w zestawie do oznaczenia jako **Opublikowany**. Po publikacji w CMS oznacz wpis również w programie; wtedy zadanie uzupełni tylko tę markę. Pełny zestaw nie uruchamia ponownie AI ani pobierania źródeł. Własne edycje nie są zastępowane przez automatyczne odświeżenie listy gotowych tekstów.

Przygotowanie w tle można wyłączyć w **Ustawieniach** i zapisać zmianę. Obowiązują dotychczasowe limity kosztów i tokenów; niepowodzenie jest widoczne na Pulpicie i w Historii. Częściowy zestaw jest wznawiany w tym samym zadaniu i budżecie, a gotowe marki nie są generowane ponownie. Gdy przygotowanie w tle jest włączone, zadanie tygodniowe również uzupełnia zestaw zamiast dokładać kolejne trzy artykuły. Ręczny przycisk „Generuj cykl 3 marek” nadal służy świadomemu przygotowaniu dodatkowego cyklu.

Synchronizacja korzysta z jednego odczytu istniejących galerii na markę zamiast osobnego zapytania dla każdej pozycji. Automatyczne przygotowanie nie sprawdza niepotrzebnych galerii innych marek; pełna kontrola wszystkich podstron `/blog` nadal obowiązuje dla każdej wybieranej galerii. Artykuły nadal mają 2200–2600 słów na język, aktualną kartę produktu i analizę zdjęć.

Nazwa programu i jasnego panelu to **WT - Blog Generator**. Identyfikator instalatora, lokalizacja danych oraz techniczne nazwy plików EXE pozostają zgodne z wcześniejszą instalacją, aby zachować bazę i klucze.

## Aktualizacja do wersji 0.4.0

1. Poczekaj na zakończenie generowania i zamknij program. Worker z Harmonogramu również musi zakończyć pracę.
2. Pobierz najnowszą zieloną paczkę **WT-Blog-Generator-Windows** z Actions, rozpakuj i uruchom **WT-Blog-Generator-Setup.exe**.
3. Zainstaluj w dotychczasowym folderze, na tym samym koncie Windows. Nie trzeba odinstalowywać starej wersji.
4. Uruchom program. Baza, artykuły, klucze API, poczta i historia pozostają w `%LOCALAPPDATA%\WheelContentManager`. Starsze ustawienia długości zmienią się automatycznie na 2200–2600 słów, a eksport zdjęć zostanie wyłączony. Budżet cyklu, ceny i dane poczty są zachowane. Nietknięte wbudowane prompty otrzymają nową wersję; własne edycje pozostają.

Dla wersji przenośnej zastąp całą paczkę programu wraz z podfolderem `Worker` w dotychczasowej lokalizacji; nie mieszaj DLL różnych wersji. Kopię zapasową katalogu danych można wykonać po zamknięciu aplikacji i Workera.

## Pierwsze uruchomienie

Kreator przeprowadza przez cztery kroki. Ustawienia kont i działania można później zmienić; długość artykułów jest ustalona według wpisu referencyjnego.

1. **AI:** wybierz OpenAI lub Anthropic, wprowadź klucz API i pobierz modele. Wybierz model obsługujący obrazy i generowanie JSON. Wpisz aktualne ceny wejścia/wyjścia na milion tokenów w USD. API jest rozliczane osobno od abonamentu ChatGPT lub Claude.
2. **E-mail:** podaj SMTP, port, STARTTLS lub SSL/TLS, login, hasło aplikacji oraz nadawcę i odbiorcę. Przycisk testowy wysyła rzeczywistą wiadomość do tego odbiorcy.
3. **Pliki:** wybierz folder eksportu. Domyślnie dokumenty są w `%LOCALAPPDATA%\WheelContentManager\Artykuly`.
4. **Prompty:** program ma już trzy zweryfikowane szablony przygotowane z dostarczonego `Wpisy na bloga.docx`. Przykłady samochodów i felg zastąpiono zmiennymi, a dane techniczne wymagają potwierdzenia ze źródeł. Możesz je przeczytać i edytować w **Prompty AI**. Import tego samego dokumentu rozpoznaje jego zawartość i przywraca gotowe szablony. Inny dokument zachowuje pełne sekcje JR/CVR/VSR, ale wymaga sprawdzenia przykładów i potwierdzenia przed generowaniem.

**Dokument redakcyjny jest dołączony:** [Wpisy na bloga.docx](docs/editorial/Wpisy%20na%20bloga.docx). Pełne teksty sekcji i opis adaptacji znajdują się w `docs/editorial`. Aktualizacja dodaje nową wersję do rozpoznanych, nietkniętych starszych szablonów wbudowanych; własne edycje użytkownika pozostają zachowane. „Przywróć domyślny” zawsze przywraca aktualny szablon z dokumentu, zachowując historię.

Zmienne szablonu: `{CAR_MAKE}`, `{CAR_MODEL}`, `{CAR_VERSION}`, `{WHEEL_BRAND}`, `{WHEEL_MODEL}`, `{WHEEL_FINISH}`, `{FRONT_SIZE}`, `{REAR_SIZE}`, `{AVAILABLE_SIZES}`, `{PRODUCT_URL}`, `{GALLERY_URL}`, `{VERIFIED_CERTIFICATIONS}`, `{PHOTO_ANALYSIS}`, `{VERIFIED_PRODUCT_DETAILS}`. Ich wartości są przekazywane jako dane JSON; dane witryn nie stają się nadrzędnymi instrukcjami AI.

## Codzienna praca

- **Sprawdź galerie** wykrywa wszystkie wpisy list, respektuje paginację i uzupełnia szczegóły trzech najnowszych niewykorzystanych konfiguracji z podanym modelem felg dla każdej marki. Pozostałe szczegóły pobierane są na żądanie — nie ma potrzeby wykonywania tysięcy zapytań przy pierwszej synchronizacji.
- W **Galeriach** wybierz samochód i kliknij **Pobierz szczegóły i zdjęcia**. Sprawdź dane, źródła i przypisanie osi. Nieznane ET, PCD, wersja samochodu czy homologacja pozostają nieznane.
- Po ręcznej korekcie potwierdź źródło parametrów i zapisz. Nie zgaduj brakujących danych. W najnowszych galeriach Concaver w dniu inspekcji brakowało nazwy felg; program raportuje ten stan.
- Domyślnie eksport zawiera same teksty. Jeśli włączysz opcjonalny eksport zdjęć, potwierdź prawo do ich użycia. Zdjęcia źródłowe są pobierane do analizy AI; eksport zdjęć jest ograniczony do tych z potwierdzonym prawem użycia.
- **Test AI bez wykorzystania galerii** wykonuje prawdziwe płatne zapytania, ale nie zapisuje artykułu, nie zużywa galerii i nie wysyła maila. Wynik można przeczytać i skopiować.
- **Zaproponuj tematy** na Pulpicie pobiera ostatnie dziesięć wpisów każdej marki, uwzględnia gotowe lokalne artykuły i pokazuje uzasadnienie. Cykl używa tej samej kolejności i przelicza ją po każdym artykule, aby różnicować modele felg. Sugestia nie zastępuje pełnej kontroli duplikatów na wszystkich podstronach `/blog`.
- Przed każdym generowaniem program ponownie pobiera oficjalną kartę produktu i potwierdza model. Opis, rozmiary i ewentualne certyfikaty trafiają do AI jako dane ze źródłami. Brak karty lub opis innego modelu blokuje generowanie. Nazwy konfiguracji i potwierdzone parametry służą naturalnej treści dla wyszukiwarek, bez zgadywania danych.
- **Generuj cykl 3 marek** wybiera niewykorzystane galerie, analizuje zdjęcia i generuje osobno PL oraz EN. Każdy język ma kontrolę programistyczną i dodatkowy audyt AI. Stała długość w interfejsie to 2200–2600 słów treści głównej na każdy język, zgodnie ze wskazanym wpisem Nissan Z / SL03 (2314 słów samej treści głównej). Błędne lub brakujące wersje nie mają statusu „Gotowy”.
- Edytuj tekst w **Artykułach**. Każdy zapis tworzy kolejną wersję. Eksport wybranego języka lub obu tworzy DOCX, HTML, TXT, metadane i folder zdjęć.
- **Zatwierdź** oznacza świadomą akceptację redakcyjną. **Oznacz jako opublikowany** zmienia wyłącznie lokalny status.

## Automatyzacja Windows

W zakładce **Harmonogram** ustaw dzień i godzinę, liczbę artykułów na markę oraz limit poprawiania. Domyślnie jest to poniedziałek 08:00 i jeden artykuł na markę. Tryb ręczny pozwala zapisać po jednej konkretnej galerii na markę; tryb automatyczny obsługuje większą liczbę artykułów. Kliknij **Zastosuj w Harmonogramie Windows**.

Worker działa przy zamkniętym interfejsie. Komputer musi być włączony, a konto Windows zalogowane. Zadanie używa `InteractiveToken`, sekretów DPAPI tego konta, `StartWhenAvailable` i `IgnoreNew`. Program nie obiecuje działania przy wyłączonym komputerze ani po wylogowaniu. Ustawianie pracy na niezalogowanym koncie wymaga osobnej konfiguracji konta zadania i bezpiecznego dostępu do jego sekretów.

Cykl tygodniowy ma trwały identyfikator tygodnia, blokadę równoległego wykonania i historię postępu. Worker zwraca kod błędu przy niepełnym cyklu lub niedostarczonym powiadomieniu. Harmonogram może wznowić zadanie po 30 minutach, w granicach ustawionego limitu prób. Ponowne uruchomienie nie generuje drugi raz ukończonego cyklu; nieukończone artykuły można wznowić. Ponowne generowanie gotowej galerii wymaga świadomego polecenia użytkownika.

Zbiorczy mail sukcesu jest wysyłany dopiero po kompletnych artykułach trzech marek z obiema wersjami. W przeciwnym razie powstaje raport częściowego wykonania. Przy niepewnym wyniku SMTP program nie ponawia automatycznie wiadomości, aby nie stworzyć duplikatu. Sprawdź wtedy skrzynkę i w ekranie Historia potwierdź odbiór albo świadomie ponów wybrane powiadomienie. SMTP nie zapewnia transakcji wspólnej z SQLite.

Worker można uruchomić ręcznie:

```powershell
.\Worker\WheelContentManager.Worker.exe --run-weekly
.\Worker\WheelContentManager.Worker.exe --sync
.\Worker\WheelContentManager.Worker.exe --diagnose
.\Worker\WheelContentManager.Worker.exe --dry-run 123
```

## Dane i bezpieczeństwo

Baza SQLite, pobrane zdjęcia, profile źródeł, historia i zaszyfrowane sekrety są w `%LOCALAPPDATA%\WheelContentManager`. Klucze nigdy nie są zapisywane do tekstowych ustawień ani repozytorium. DPAPI wymaga tego samego konta Windows. Puste pole klucza lub hasła zachowuje dotychczasowy sekret.

Wykonuj kopię katalogu danych po zamknięciu aplikacji i Workera. Przed odinstalowaniem wyłącz zadanie w Harmonogramie. Odinstalowanie nie usuwa artykułów ani bazy.

Koszty kontrolowane są przez limity tokenów, konserwatywną rezerwę wywołania i ceny wpisane przez użytkownika. Wykorzystanie tokenów jest zapisane w zadaniach i wersjach. Po zerwaniu połączenia bez odpowiedzi pozostaje rezerwa — nie jest zakładany zerowy koszt. Rzeczywisty rachunek określa dostawca API.

## Kompilacja ze źródeł

Zainstaluj Visual Studio z obsługą .NET 10 i aplikacji desktopowych WPF (np. Visual Studio 2026) oraz SDK zgodny z `global.json`. Otwórz `WheelContentManager.sln`.

```powershell
dotnet restore WheelContentManager.sln --locked-mode
dotnet build WheelContentManager.sln -c Release --no-restore
dotnet test tests/WheelContentManager.Tests -c Release
.\scripts\publish-windows.ps1
```

Instalator: po publikacji uruchom kompilator Inno Setup 6 dla `scripts\installer.iss`. GitHub Actions wykonuje kompilację, testy, diagnostykę Workera i budowanie paczki oraz instalatora na Windows.

Migracje bazy uruchamiają się automatycznie przy starcie. Do tworzenia kolejnych migracji:

```powershell
dotnet tool restore
dotnet ef migrations add NazwaMigracji --project src/WheelContentManager.Infrastructure
```

## Sprawdzone i niesprawdzone

Aktualne dowody walidacji oraz ograniczenia opisuje [docs/WERYFIKACJA.md](docs/WERYFIKACJA.md). Kompilacja WPF na Linux nie dowodzi, że interfejs działa na Windows 11. Testy mock AI nie dowodzą jakości rzeczywistych artykułów. Pełny scenariusz odbioru wymaga dokumentu redakcyjnego, skonfigurowanych API/SMTP i próby na Windows.

Szczegóły zbadanych witryn: [docs/ZRODLA.md](docs/ZRODLA.md).

## Praca w Codex

Każde zadanie działa w osobnym środowisku. Używaj istniejącej kopii `/workspace/blog`; nie twórz dodatkowego worktree bez wyraźnej prośby. `scripts/setup-cloud.sh` przygotowuje SDK, zależności, kompilację i testy. WPF, DPAPI i Harmonogram Windows wymagają Windows; w chmurze Linux można pracować nad kodem, bazą i testami usług.

Przed wyborem galerii i wywołaniem AI program sprawdza wszystkie podstrony `/blog` danej marki oraz treść wpisów. Wspólny adres galerii lub zdjęcia blokuje nowy artykuł. Zgodność modelu samochodu i felg oznacza możliwy duplikat i również zatrzymuje generowanie do sprawdzenia. Status oraz adres istniejącego wpisu są widoczne przy galerii. Błąd sieci, zmiana HTML lub niepełna paginacja nie są uznawane za brak artykułu. Po ścisłym dopasowaniu kontrola może zakończyć się od razu, ponieważ publikacja tej galerii jest już potwierdzona. Brak dopasowania wymaga kompletnego skanowania. Wyniki pełnego skanowania i potwierdzone dopasowania są przechowywane w pamięci przez maksymalnie 15 minut; pierwsza kontrola może potrwać kilka minut. Automatyczny wybór przechodzi do kolejnej galerii, gdy wykryje opublikowany wpis. Regenerowanie i tryb testowy również korzystają z tej kontroli.

Diagnostyka pojedynczej galerii bez kosztu AI: `Worker/WheelContentManager.Worker.exe --check-blog <ID galerii>`.
