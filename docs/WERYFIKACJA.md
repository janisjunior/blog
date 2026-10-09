# Weryfikacja — 9 października 2026

## Wykonane w środowisku Linux

- Zainstalowano oficjalny SDK .NET 10.0.401; SHA512 archiwum zgadza się z metadanymi wydania Microsoft.
- `scripts/setup-cloud.sh`: odtwarzanie zależności z lockfile, kompilacja wszystkich siedmiu projektów (również WPF przez EnableWindowsTargeting), testy.
- Kompilacja: 0 błędów, 0 ostrzeżeń.
- 80 testów: 80 zaliczonych, 0 niezaliczonych, 0 pominiętych. Testy sprawdzają migracje SQLite, deduplikację, korekty, osie i rozmiary, pełne sekcje DOCX, prompty, JSON, intro, jakość, podobieństwo, kontrakty HTTP OpenAI/Anthropic, generowanie PL/EN z mock AI i obrazami, ograniczenia prób/kosztów, brak zużycia galerii w dry-run, historię regenerowania, dokumenty i zdjęcia w DOCX/HTML/TXT/ZIP, XML harmonogramu, blokadę procesów, warunki pełnego powiadomienia oraz jego niepewny stan i ręczne potwierdzenie.
- Testy dokumentu: oryginalny DOCX, kompletność wszystkich linii wymagań, dynamiczne dane galerii, brak wycieku konfiguracji przykładowych, gotowe szablony przy instalacji, idempotentny import, rozpoznawanie treści po SHA256 oraz aktualizacja bez nadpisania edycji użytkownika.
- Testy /blog: wszystkie trzy rzeczywiste struktury HTML, paginacja, wspólne zdjęcia, możliwy duplikat, pomijanie nawigacji i polecanych wpisów, błąd sieci i brak kosztu AI przy blokadzie. Test pełnego klienta HTTP wykrywa wpis z drugiej strony i sprawdza, że niepełny skan nie trafia do cache.
- Testy na lokalnych fragmentach rzeczywistego HTML wszystkich trzech marek. Oddzielna syntetyczna próbka nie jest przedstawiana jako źródło produkcyjne.
- Rzeczywista synchronizacja: JR 1465 galerii, Concaver 1115, Vesser 139; łącznie 2719. Pierwszy przebieg pobrał szczegóły 9 galerii, parametry, pełne URL zdjęć i odpowiednie dane produktów.
- Druga rzeczywista synchronizacja: 0 nowych rekordów dla każdej marki; liczba galerii nie wzrosła. Odczyt prawdziwych galerii nie wymagał AI ani wysyłania maili.
- Rzeczywista kontrola `/blog` wykryła istniejący wpis JR dla galerii Nissan Z (`/vehicle-gallery/2700`) i Concaver dla Mercedes-Benz E63s (`/gallery/1560`) na podstawie wspólnych zdjęć. Pełny skan Vesser nie znalazł wpisu dla sprawdzanej galerii Audi RS3 (`/gallery/audi-rs3-243`).
- Starsza podstrona JR `/blog?page=11` podczas próby zwróciła timeout. Program traktuje niepełne sprawdzanie jako błąd i blokuje generowanie; ścisłe dopasowanie istniejącego wpisu może zakończyć kontrolę wcześniej.
- Publikacja samodzielnych aplikacji Desktop i Worker dla `win-x64`. Pliki EXE i zależności można przygotować na Linux, lecz nie uruchomić tutaj interfejsu Windows.

## Wykonane przez GitHub Actions na Windows

[Uruchomienie dla pierwszej kompletnej implementacji](https://github.com/janisjunior/blog/actions/runs/37914282313) zakończyło się sukcesem: kompilacja, testy, publikacja samodzielnej paczki Windows, uruchomienie Workera z nową bazą SQLite, kompilacja instalatora Inno Setup i zapis paczki ZIP oraz Setup.exe jako artefakt. To potwierdza automatyczne budowanie i start Workera na Windows, lecz nie zastępuje ręcznego scenariusza WPF na Windows 11. [Kolejny przebieg](https://github.com/janisjunior/blog/actions/runs/37915063823) również zakończył się sukcesem, włącznie z obsługą timeoutów i wcześniejszym zakończeniem kontroli po ścisłym dopasowaniu. [Najnowszy przebieg](https://github.com/janisjunior/blog/actions/runs/37915813654) również zakończył wszystkie etapy sukcesem i udostępnił artefakt `WheelContentManager-Windows`: ZIP oraz instalator. Obejmuje ostatnie zmiany przenoszące długie operacje poza wątek interfejsu i kończące kontrolę po potwierdzeniu publikacji przed pobieraniem niepowiązanych starszych podstron.

## Dokument redakcyjny

- Dostarczono i odczytano pełny `Wpisy na bloga.docx` (JR/CVR/VSR). Zweryfikowano zachowanie wymagań, zastąpienie danych przykładowych zmiennymi, oddzielne zadania PL/EN i warunkowe użycie rozmiarów/TÜV. Szablony są gotowe po instalacji; aktualizacja zachowuje historię oraz własne edycje.
## Niewykonane i warunki pełnego odbioru

- Nie było kluczy OpenAI/Anthropic ani ustawień SMTP w środowisku. Nie wykonano płatnych żądań AI i nie wysłano rzeczywistego maila. Mocki nie dowodzą jakości artykułów ani dostępności danego modelu.
- Nie uruchomiono ręcznie WPF na Windows 11; nie sprawdzono ręcznie interfejsu, DPAPI, rejestracji rzeczywistego zadania ani pełnego cyklu przy zamkniętym UI.
- Instalator zbudowano automatycznie na runnerze Windows; nie przeprowadzono jego ręcznej instalacji na Windows 11.
- Nie przeprowadzono ręcznej oceny sześciu gotowych tekstów, powiadomienia z ZIP i kolejnego uruchomienia Harmonogramu Windows. To końcowy scenariusz odbioru po konfiguracji.

## Próba na Windows 11

1. Pobierz paczkę z udanego uruchomienia GitHub Actions lub zbuduj ją przez `scripts/publish-windows.ps1`.
2. Otwórz Desktop, przejdź kreator i zapisz ustawienia API/SMTP. Klucze wpisuj wyłącznie do PasswordBox w aplikacji.
3. Przeczytaj trzy wbudowane szablony z dokumentu. Sprawdź, że dane w podglądzie odpowiadają wybranej galerii, a nie przykładom BMW/JR50 i Audi/CVR5. Własne nowe dokumenty nadal wymagają potwierdzenia.
4. Sprawdź galerie; zobacz prawdziwe dane i zdjęcia jednej galerii każdej marki. Niekompletna galeria powinna jasno pokazać braki.
5. Wykonaj test AI na jednej galerii — sprawdź, że nie zużywa galerii i nie wysyła maila.
6. Uruchom cykl trzech marek. Sprawdź trzy artykuły i sześć niezależnych wersji, wyniki jakości i koszty.
7. Edytuj tekst, sprawdź historię, eksport DOCX/HTML/TXT i zdjęcia z potwierdzonym prawem użycia.
8. Sprawdź pojedynczy mail i ZIP, a także raport częściowy przy błędzie. Ręczne ponowienie maila wymaga świadomego potwierdzenia.
9. Zarejestruj harmonogram, zamknij Desktop, uruchom zadanie na tym samym zalogowanym koncie Windows i sprawdź wynik. Komputer musi być włączony.
10. Powtórz synchronizację i cykl tego samego tygodnia; sprawdź brak duplikatów i zachowanie historii.

Projekt nie jest opisany jako w pełni odebrany, dopóki powyższe warunki nie zostaną sprawdzone. Aktualne pliki i testy umożliwiają wykonanie tej próby bez tworzenia makiet funkcji.
