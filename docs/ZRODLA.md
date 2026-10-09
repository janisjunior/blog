# Inspekcja źródeł — 9 października 2026

Profile XPath w projekcie GalleryProviders utworzono po pobraniu rzeczywistych stron. Nie wynikają z domysłów. Fragmenty HTML w `tests/WheelContentManager.Tests/Fixtures/Live` pochodzą z tych stron; usunięto niepowiązaną nawigację i skrypty. Syntetyczny `Fixtures/gallery.html` jest osobno oznaczonym testem silnika.

## JR Wheels

- Lista `https://jr-wheels.com/vehicle-gallery`: 1465 wpisów w HTML. Karty `.subsite_gallery_box`, linki `vehicle-gallery/<id>`, filtr marek `select[name=producent]`. Filtrowanie jest w JavaScript, lecz same dane są obecne w HTML.
- Sprawdzono `https://jr-wheels.com/vehicle-gallery/2703`: Ford Focus ST, JR46, Silver Machined Face, 19x8.5 przód i tył. Pełne zdjęcia są w linkach lightbox w `.new-gallery-grid`.
- Etykiety `gallery_info` zawierają dane osi i auta. Tytuł jest bardziej szczegółowy od ogólnego modelu w filtrze; przechowywane jest również źródło ogólnej grupy modelu.
- Link do karty produktu wskazuje `https://jr-wheels.com/category/jr46`. Zweryfikowane dostępne średnice to 18, 19, 20. Ogólny link do TÜV w nawigacji nie dowodzi certyfikacji tej konfiguracji.
- `robots.txt` zezwala na galerię, zabrania części operacji konta i koszyka.

## Concaver Wheels

- Lista `https://concaverwheels.com/vehicle_gallery`: 1115 kart w HTML. Linki szczegółowe mają postać `gallery/<id>`; filtr marek `select[name=brand]`.
- Najnowsze dziewięć kart nie podaje modelu felg. Sprawdzono również szczegóły `gallery/1597` — pole modelu jest rzeczywiście puste. Nie wolno przypisywać modelu na podstawie wykończenia lub ID filtra.
- Najnowsza karta z podanym modelem przy inspekcji to `https://concaverwheels.com/gallery/1560`: Mercedes E63s, Concaver CVR1, Brushed Bronze, 20x9.5 przód i 20x10 tył.
- Szczegóły i lightbox mają strukturę podobną do JR. Produkt `https://concaverwheels.com/cvr1` potwierdza Hybrid Forged i średnice 19, 20, 21, 22, 23. Sprawdzone CVR5 ma inny zakres, więc nie przenosi się 19–23 automatycznie między modelami.
- `/robots.txt` zwrócił HTML strony głównej (soft 404), bez dyrektyw robots. Pobieranie zachowuje ograniczenie częstotliwości i zatrzymuje się przy odmowie dostępu.

## Vesser Forged

- `https://vesserforged.com/galleries/` udostępnia galerię pojazdów. Karty `.galleries .gallery-grid`, linki `gallery/<slug>-<id>`.
- Przy inspekcji 14 stron paginacji i 139 galerii. Link następnej strony jest w `nav.pagination a.next`. Dane są w HTML; do tego workflow Playwright nie jest wymagany.
- Sprawdzono `https://vesserforged.com/gallery/bmw-5-series-246`: BMW 5 series, VSR1, Satin Bronze, przód 21x9, tył 21x10,5. Przecinek normalizuje się bez zmiany osi.
- Zdjęcia są tylko w `section.gallery > .grid`, dzięki czemu nie dołącza się zdjęć „You may also like”. W tym przykładzie jest 26 pełnych zdjęć.
- Link produktu `https://vesserforged.com/wheel/vsr1` potwierdza model i forged; jego własna lista średnic to 19–23. VSR9 ma inny zakres — 21 i 22.
- Odrębne galerie produktów `/galleries/wheels` oraz filmów `/galleries/video` nie są pobierane.
- `robots.txt` zezwala na pobieranie.

## Aktualizacje struktury

Profile domyślne są kopiowane do AppData/Profiles przy pierwszym uruchomieniu. Nie nadpisują lokalnie zmienionego profilu. Po zmianie witryny ponownie zbadaj HTML, zaktualizuj profil i próbki, uruchom testy oraz synchronizację. Brak selektora lub źródła wywołuje czytelny błąd — nie dane zastępcze. Nowy profil wymaga opisu dowodu inspekcji i flagi Verified.

Synchronizacja odczytuje całą listę i paginację; szczegóły są pobierane w niewielkich partiach lub na żądanie. Każdy klient źródła ogranicza częstotliwość, sprawdza robots i stosuje ograniczone ponowienia błędów 429/5xx. Nie stosuje się omijania CAPTCHA, TLS ani mechanizmów dostępu.

Źródła mogą zawierać ogólne marketingowe twierdzenia o lekkości lub certyfikacji. Parametry konkretnej konfiguracji nie są wnioskowane z wyglądu ani ogólnej nawigacji. Zakres certyfikacji pozostaje niepotwierdzony, jeśli nie ma osobnego, właściwego dowodu. Wersja aplikacji nie pobiera automatycznie i nie interpretuje dokumentów homologacji PDF; wymagają potwierdzenia redakcyjnego z właściwym źródłem.

## Wpisy już opublikowane

Potwierdzono listy `/blog` wszystkich trzech marek, paginację JR przez `?page=`, Concaver przez `/blog/<numer>` i Vesser przez `/blog/<numer>/`. Treść JR/Concaver znajduje się w `.blog-content`, a Vesser w `.blog-header` oraz `.subpage-content-blog`. Kontrola pobiera wszystkie strony i wpisy; porównuje adresy galerii oraz zdjęcia z treści, pomijając nawigację, stopkę i polecane wpisy. Skopiowane do oddzielnego folderu, przemianowane zdjęcia mogą nie dać ścisłego dopasowania; zgodność samochodu i felg jest dodatkowym ostrzeżeniem. Kontrola nie stosuje rozpoznawania wizualnego identycznych zdjęć i nie gwarantuje wykrycia wpisu, który nie udostępnia wspólnych identyfikatorów ani nazw.
