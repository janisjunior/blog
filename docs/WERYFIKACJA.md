# Weryfikacja — 9 października 2026

## Wykonane w środowisku Linux

- Zainstalowano oficjalny SDK .NET 10.0.401; SHA512 archiwum zgadza się z metadanymi wydania Microsoft.
- `scripts/setup-cloud.sh`: odtwarzanie zależności z lockfile, kompilacja wszystkich siedmiu projektów (również WPF przez EnableWindowsTargeting), testy.
- Kompilacja: 0 błędów, 0 ostrzeżeń.
- 68 testów: 68 zaliczonych, 0 niezaliczonych, 0 pominiętych. Testy sprawdzają migracje SQLite, deduplikację, korekty, osie i rozmiary, pełne sekcje DOCX, prompty, JSON, intro, jakość, podobieństwo, kontrakty HTTP OpenAI/Anthropic, generowanie PL/EN z mock AI i obrazami, ograniczenia prób/kosztów, brak zużycia galerii w dry-run, historię regenerowania, dokumenty i zdjęcia w DOCX/HTML/TXT/ZIP, XML harmonogramu, blokadę procesów, warunki pełnego powiadomienia oraz jego niepewny stan i ręczne potwierdzenie.
- Testy /blog: wszystkie trzy rzeczywiste struktury HTML, paginacja, wspólne zdjęcia, możliwy duplikat, pomijanie nawigacji i polecanych wpisów, błąd sieci i brak kosztu AI przy blokadzie. Test pełnego klienta HTTP wykrywa wpis z drugiej strony i sprawdza, że niepełny skan nie trafia do cache.
- Testy na lokalnych fragmentach rzeczywistego HTML wszystkich trzech marek. Oddzielna syntetyczna próbka nie jest przedstawiana jako źródło produkcyjne.
- Rzeczywista synchronizacja: JR 1465 galerii, Concaver 1115, Vesser 139; łącznie 2719. Pierwszy przebieg pobrał szczegóły 9 galerii, parametry, pełne URL zdjęć i odpowiednie dane produktów.
- Druga rzeczywista synchronizacja: 0 nowych rekordów dla każdej marki; liczba galerii nie wzrosła. Odczyt prawdziwych galerii nie wymagał AI ani wysyłania maili.
- Publikacja samodzielnych aplikacji Desktop i Worker dla `win-x64`. Pliki EXE i zależności można przygotować na Linux, lecz nie uruchomić tutaj interfejsu Windows.

## Niewykonane i warunki pełnego odbioru

- Nie dostarczono `Wpisy na bloga.docx`. Importer i trzy oddzielne szablony istnieją, ale pełna zgodność redakcyjna z tym dokumentem pozostaje niezweryfikowana. Domyślne prompty wymagają potwierdzenia i nie uruchamiają generowania samodzielnie.
- Nie było kluczy OpenAI/Anthropic ani ustawień SMTP w środowisku. Nie wykonano płatnych żądań AI i nie wysłano rzeczywistego maila. Mocki nie dowodzą jakości artykułów ani dostępności danego modelu.
- Nie uruchomiono WPF na Windows 11; nie sprawdzono ręcznie interfejsu, DPAPI, rejestracji rzeczywistego zadania ani pełnego cyklu przy zamkniętym UI.
- Przygotowano Inno Setup i workflow GitHub Actions dla Windows. Sam skrypt instalatora nie oznacza, że instalator został zbudowany i sprawdzony w tej maszynie Linux.
- Nie przeprowadzono ręcznej oceny sześciu gotowych tekstów, powiadomienia z ZIP i kolejnego uruchomienia Harmonogramu Windows. To końcowy scenariusz odbioru po konfiguracji.

## Próba na Windows 11

1. Pobierz paczkę z udanego uruchomienia GitHub Actions lub zbuduj ją przez `scripts/publish-windows.ps1`.
2. Otwórz Desktop, przejdź kreator i zapisz ustawienia API/SMTP. Klucze wpisuj wyłącznie do PasswordBox w aplikacji.
3. Zaimportuj dokument, sprawdź trzy szablony, zastąp przykłady zmiennymi i potwierdź zgodność.
4. Sprawdź galerie; zobacz prawdziwe dane i zdjęcia jednej galerii każdej marki. Niekompletna galeria powinna jasno pokazać braki.
5. Wykonaj test AI na jednej galerii — sprawdź, że nie zużywa galerii i nie wysyła maila.
6. Uruchom cykl trzech marek. Sprawdź trzy artykuły i sześć niezależnych wersji, wyniki jakości i koszty.
7. Edytuj tekst, sprawdź historię, eksport DOCX/HTML/TXT i zdjęcia z potwierdzonym prawem użycia.
8. Sprawdź pojedynczy mail i ZIP, a także raport częściowy przy błędzie. Ręczne ponowienie maila wymaga świadomego potwierdzenia.
9. Zarejestruj harmonogram, zamknij Desktop, uruchom zadanie na tym samym zalogowanym koncie Windows i sprawdź wynik. Komputer musi być włączony.
10. Powtórz synchronizację i cykl tego samego tygodnia; sprawdź brak duplikatów i zachowanie historii.

Projekt nie jest opisany jako w pełni odebrany, dopóki powyższe warunki nie zostaną sprawdzone. Aktualne pliki i testy umożliwiają wykonanie tej próby bez tworzenia makiet funkcji.
