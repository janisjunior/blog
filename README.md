# WT - Blog Generator 0.6.0

Program do wybierania galerii JR Wheels, Concaver Wheels i Vesser Forged oraz przygotowywania kompletnych plików do własnego czatu ChatGPT. Jasny panel: wybór marki, lista galerii, podgląd zdjęcia i zapis pliku. Bez importowania odpowiedzi, zatwierdzania artykułów, API, SMTP i automatycznego generowania tekstów.

## Jak używać

1. Wybierz **JR Wheels**, **Concaver Wheels** albo **Vesser Forged**. Przy pierwszym wyborze program pobierze listę galerii i sprawdzi archiwum `/blog`.
2. Wyszukaj samochód lub felgi i wybierz galerię. Lista pomija wykryte oraz możliwe dopasowania do wpisów na stronie. Lokalny tekst zapisany w poprzedniej wersji nie blokuje galerii.
3. Kliknij **Otwórz galerię zdjęć**, aby zobaczyć źródło i pobrać wybrane zdjęcia. Podgląd i przycisk **Otwórz zdjęcie** pomagają odnaleźć zdjęcie tej konfiguracji.
4. Wybierz folder i kliknij **Przygotuj plik do ChatGPT**. Program odświeży szczegóły galerii, sprawdzi aktualne `/blog` i oficjalną kartę modelu, a następnie otworzy folder gotowych plików.
5. Wrzuć **Wrzuc do ChatGPT.docx** do swojego czatu. Dokument zawiera pełne polecenie, zweryfikowane dane, wymagania PL/EN, klikalne źródła i zdjęcie JPG/PNG, jeśli zostało pobrane. Nie trzeba kopiować oddzielnego polecenia. Zdjęcie jest też osobnym plikiem w folderze. Jeśli formatem zdjęcia jest WebP, dołącz je osobno. Awaria pobrania zdjęcia nie usuwa dokumentu ani linków do galerii.

Polecenie prosi o dwa niezależne artykuły po 2200–2600 słów. Zawiera kompletne wymagania z dostarczonego [dokumentu redakcyjnego](docs/editorial/Wpisy%20na%20bloga.docx). Nie wymyśla danych niepotwierdzonych. Działanie osobistego ChatGPT zależy od wybranego modelu i dostępnych limitów konta; program nie loguje się do niego i nie wywołuje API.

## Sprawdzanie dostępności

Archiwum wszystkich podstron `/blog` jest pobierane raz dla sprawdzanej marki, a następnie porównywane z całą listą. Lista zapamiętuje datę sprawdzenia i jest szybko dostępna przy kolejnych wejściach. **Sprawdź /blog i odśwież listę** aktualizuje ją na żądanie. Nie są pobierane pełne szczegóły kilku tysięcy galerii naraz; sprawdzenie listy używa dostępnych danych i miniatur, a ostateczna kontrola pełnej galerii i zdjęć odbywa się przed eksportem.

Potwierdzone i możliwe duplikaty oraz nieudane sprawdzenie archiwum blokują zapis pliku. Aktualna karta modelu musi pasować do wybranej konfiguracji. Pierwsze sprawdzanie stron może trwać kilka minut; przycisk **Anuluj** przerywa operację. Program respektuje robots.txt i limity żądań.

## Pobieranie i aktualizacja

Na GitHubie otwórz najnowsze udane uruchomienie workflow Windows i pobierz **Artifacts → WT-Blog-Generator-Windows**. Rozpakuj ZIP i uruchom **WT-Blog-Generator-Setup.exe**. Przed aktualizacją zakończ starszą wersję programu i jej zadania w tle; po instalacji uruchom komputer ponownie.

Dane starszej wersji w `%LOCALAPPDATA%\WheelContentManager` pozostają zachowane. Nowy interfejs nie wykorzystuje statusu lokalnego artykułu do blokowania galerii. Usuwany jest dawny autostart, stare zadania są wyłączane, a Worker nie wykonuje automatycznego generowania ani testów AI. Zamknięcie okna kończy program; nie pozostaje ikona zasobnika ani generator w tle. Zapisane sekrety nie są kasowane, lecz nowy panel ich nie używa.

## Rozwój i sprawdzenie

Aplikacja jest natywnym WPF na Windows. SDK: .NET 10.0.401. W chmurze użyj istniejącego `/workspace/blog`, bez tworzenia dodatkowego worktree. `bash scripts/setup-cloud.sh` weryfikuje SDK, przywraca zależności z lockfile, buduje rozwiązanie i uruchamia testy. Linux nie uruchamia WPF; workflow Windows buduje instalator i sprawdza Worker.

Stare modele, migracje i testy pozostają w bibliotekach dla zgodności danych. Szczegóły źródeł i weryfikacji: [ZRODLA.md](docs/ZRODLA.md), [WERYFIKACJA.md](docs/WERYFIKACJA.md). Interaktywne działanie Windows i wrzucenie dokumentu do osobistego ChatGPT wymagają ręcznej weryfikacji.
