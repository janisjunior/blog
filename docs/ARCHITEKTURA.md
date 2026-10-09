# Architektura

`WheelContentManager.sln` zawiera siedem projektów .NET 10:

| Projekt | Odpowiedzialność |
| --- | --- |
| Core | Modele, fakty i źródła, interfejsy IGalleryProvider/IAiProvider, normalizacja i kontrola jakości |
| GalleryProviders | Trzy dostawcy, potwierdzone profile XPath, HTML, robots, ograniczenia zapytań, opcjonalny Playwright |
| AI | Klienci OpenAI/Anthropic, obrazy, strukturalne odpowiedzi, modele, użycie tokenów, ograniczone ponowienia |
| Infrastructure | EF Core/SQLite i migracje, ustawienia, DPAPI, wersjonowanie, przebieg cyklu, SMTP, eksport, Harmonogram Windows |
| Desktop | WPF i CommunityToolkit.Mvvm, kreator, widoki, polecenia i anulowanie |
| Worker | CLI i automatyzacja przy zamkniętym interfejsie, wspólne usługi biznesowe |
| Tests | Testy jednostkowe i integracyjne SQLite, mock AI/HTTP, rzeczywiste lokalne próbki HTML |

## Przebieg

Synchronizacja odkrywa wpisy indeksu i zapisuje unikalne, znormalizowane URL. Osobne pobieranie szczegółów dodaje konfigurację osi, pełne zdjęcia oraz fakty z produktu. Ręczne korekty mają znacznik Manual i nie są automatycznie nadpisywane. Średnica/szerokość wynikają z zapisanego rozmiaru, bez zamiany osi.

Cykl blokuje równoległą pracę przez plik z wyłącznym dostępem. Wybiera najnowsze niewykorzystane kompletne galerie albo ręcznie wskazane ID, bez zastępowania marki inną. Tryb ręczny cyklu wybiera jeden samochód dla każdej marki; wiele artykułów na markę obsługuje tryb automatyczny.

Generowanie najpierw sprawdza kompletność galerii i potwierdzenie promptu. Potem pobiera reprezentatywne zdjęcia, uruchamia analizę wizualną, osobne zadanie PL, osobne EN oraz audyt każdego języka. Każde wywołanie zapisuje rezerwę tokenów przed zapytaniem i rzeczywiste wykorzystanie po odpowiedzi. Dane źródłowe są w części danych JSON, a nie nadrzędnej instrukcji.

Próby naprawy są ograniczone. Błędny JSON, zbyt krótkie treści, niepotwierdzone parametry, powielanie i błędy audytu nie dają statusu Ready. Po awarii pozostaje zadanie i częściowy artykuł. Ponowienie wznawia nieukończone wersje. Jedna galeria ma jeden rekord Article; świadome regenerowanie tworzy kolejne ArticleVersions, zachowując historię.

Mail pełnego sukcesu wymaga wszystkich marek i języków. NotificationHistory ma unikalny klucz cyklu/rodzaju raportu. Stan Sending jest zapisany przed transmisją, a Sent po niej. Przy niepewności nie ma automatycznego ponowienia; użytkownik może sprawdzić odbiór i świadomie potwierdzić lub ponowić wiadomość w historii.

## Przechowywanie

SQLite ma tabele Brands, Vehicles, Galleries, GalleryImages, WheelSpecifications, SourceReferences, Articles, ArticleVersions, PromptTemplates, PromptVersions, GenerationJobs, AutomationRuns, NotificationHistory, ApplicationSettings i ErrorLogs. Migracje są w repozytorium i stosowane przy starcie. Klucz GalleryId artykułu, URL galerii, identyfikatory cykli i powiadomień oraz rewizje mają indeksy unikalności.

IDbContextFactory daje osobny kontekst na operację. Dane są w AppData, instalacja w osobnym katalogu. Sekrety mają pliki binarne DPAPI CurrentUser; nie trafiają do ApplicationSettings ani logów. Nie można przenieść ich do innego konta samą kopią plików.

## Rozszerzenia

Nowa marka wymaga własnego IGalleryProvider, profilu zbadanego źródła i szablonu promptu. Nowy dostawca AI implementuje IAiProvider. UI i Worker korzystają z tego samego kontenera DI. Playwright jest opcjonalnym trybem profilu; obecne trzy źródła nie wymagają wykonywania JavaScript. Dla profilu JavaScript trzeba zainstalować przeglądarkę Playwright i zweryfikować zgodność ograniczeń witryny.

Desktop i Worker są publikowane z osobnymi zestawami zależności. Worker znajduje się w podfolderze `Worker`; nie łączy się DLL z runtime WPF, które mogą różnić się od pakietów konsolowych (np. System.IO.Packaging). Harmonogram wskazuje EXE w tym podfolderze.
