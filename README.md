# Wheel Content Manager

Aplikacja desktopowa dla Windows 11, przygotowująca artykuły o konfiguracjach samochodów na felgach JR Wheels, Concaver Wheels i Vesser Forged. Interfejs jest po polsku, a treści PL i EN powstają w osobnych zadaniach AI. Aplikacja nie publikuje artykułów na stronach.

## Jak uruchomić — bez Visual Studio

1. Otwórz [zakładkę Actions repozytorium](https://github.com/janisjunior/blog/actions).
2. Wybierz zakończone zielonym znacznikiem uruchomienie **Windows — kompilacja, testy i paczka EXE**.
3. Na dole, w **Artifacts**, pobierz **WheelContentManager-Windows** (GitHub wymaga zalogowania do pobrania).
4. Rozpakuj pobrany ZIP. Uruchom `WheelContentManager-Setup.exe`, jeśli instalator jest w paczce. Alternatywnie rozpakuj wewnętrzny `WheelContentManager-win-x64.zip` do stałego folderu i uruchom `WheelContentManager.Desktop.exe`.
5. Zachowaj wszystkie pliki paczki razem, w tym podfolder `Worker`. Wersja samodzielna zawiera .NET — nie trzeba instalować SDK.

Paczka jest niepodpisana cyfrowo. Windows może wyświetlić informację o nieznanym wydawcy. Nazwa repozytorium pozostaje `blog`; nazwa programu to Wheel Content Manager.

## Pierwsze uruchomienie

Kreator przeprowadza przez cztery kroki. Wszystkie ustawienia można później zmienić.

1. **AI:** wybierz OpenAI lub Anthropic, wprowadź klucz API i pobierz modele. Wybierz model obsługujący obrazy i generowanie JSON. Wpisz aktualne ceny wejścia/wyjścia na milion tokenów w USD. API jest rozliczane osobno od abonamentu ChatGPT lub Claude.
2. **E-mail:** podaj SMTP, port, STARTTLS lub SSL/TLS, login, hasło aplikacji oraz nadawcę i odbiorcę. Przycisk testowy wysyła rzeczywistą wiadomość do tego odbiorcy.
3. **Pliki:** wybierz folder eksportu. Domyślnie dokumenty są w `%LOCALAPPDATA%\WheelContentManager\Artykuly`.
4. **Prompty:** zaimportuj `Wpisy na bloga.docx`. Dokument musi zawierać osobne nagłówki JR, CVR i VSR. Import zachowuje całą treść sekcji. W zakładce **Prompty AI** zastąp konkretne przykłady zmiennymi, sprawdź wymagania każdej marki i zaznacz potwierdzenie zgodności. Dopiero potwierdzone szablony mogą generować artykuły.

**Dokument redakcyjny nie został dostarczony z opisem projektu.** Wbudowany szablon zawiera wymagania z opisu, ale pozostaje niezatwierdzony. Program nie udaje, że zastępuje brakujący dokument.

Zmienne szablonu: `{CAR_MAKE}`, `{CAR_MODEL}`, `{CAR_VERSION}`, `{WHEEL_BRAND}`, `{WHEEL_MODEL}`, `{WHEEL_FINISH}`, `{FRONT_SIZE}`, `{REAR_SIZE}`, `{AVAILABLE_SIZES}`, `{PRODUCT_URL}`, `{GALLERY_URL}`, `{VERIFIED_CERTIFICATIONS}`, `{PHOTO_ANALYSIS}`, `{VERIFIED_PRODUCT_DETAILS}`. Ich wartości są przekazywane jako dane JSON; dane witryn nie stają się nadrzędnymi instrukcjami AI.

## Codzienna praca

- **Sprawdź galerie** wykrywa wszystkie wpisy list, respektuje paginację i uzupełnia szczegóły trzech najnowszych niewykorzystanych konfiguracji z podanym modelem felg dla każdej marki. Pozostałe szczegóły pobierane są na żądanie — nie ma potrzeby wykonywania tysięcy zapytań przy pierwszej synchronizacji.
- W **Galeriach** wybierz samochód i kliknij **Pobierz szczegóły i zdjęcia**. Sprawdź dane, źródła i przypisanie osi. Nieznane ET, PCD, wersja samochodu czy homologacja pozostają nieznane.
- Po ręcznej korekcie potwierdź źródło parametrów i zapisz. Nie zgaduj brakujących danych. W najnowszych galeriach Concaver w dniu inspekcji brakowało nazwy felg; program raportuje ten stan.
- Potwierdź prawo do użycia zdjęć, które mają trafić do eksportu. Zdjęcia źródłowe są pobierane do analizy AI; eksport zdjęć jest ograniczony do tych z potwierdzonym prawem użycia.
- **Test AI bez wykorzystania galerii** wykonuje prawdziwe płatne zapytania, ale nie zapisuje artykułu, nie zużywa galerii i nie wysyła maila. Wynik można przeczytać i skopiować.
- **Generuj cykl 3 marek** wybiera niewykorzystane galerie, analizuje zdjęcia i generuje osobno PL oraz EN. Każdy język ma kontrolę programistyczną i dodatkowy audyt AI. Domyślna długość to 1200–1800 słów. Błędne lub brakujące wersje nie mają statusu „Gotowy”.
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
