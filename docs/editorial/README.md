# Dokument i adaptacja promptów

`Wpisy na bloga.docx` jest oryginalnym dokumentem dostarczonym przez użytkownika. SHA256: `7c5a625e388ad16842d244c07be70f43fd8c2a5b9fed339f1549acbdce305d89`.

Pliki JR-source.md, CVR-source.md i VSR-source.md zachowują pełny tekst każdej sekcji. Wbudowane, zweryfikowane szablony znajdują się w `src/WheelContentManager.Infrastructure/Templates` i zawierają wszystkie wymagania tych sekcji, z następującymi adaptacjami wynikającymi ze specyfikacji aplikacji:

- Konfiguracje BMW 3 Series / M3, JR50, Matt Bronze oraz Audi A3 / S3 / RS3, Concaver CVR5, Gloss Blue-Purple Chameleon są przykładami. Pola Model, Colour/Finish, Front size, Rear size, Car make i Car model zastąpiono zmiennymi. Nie zamieniono osi w przykładzie CVR, gdzie przednia felga jest szersza.
- `[car model]` i `[model auta]` odwołują się do `{CAR_MODEL}`. Karta produktu, specyfikacja i galeria mają własne zmienne. VSR otrzymał jawny blok danych wybranej galerii, ponieważ jego sekcja nie zawiera konkretnej konfiguracji.
- Wymóg TÜV nie upoważnia do wymyślania homologacji. Szablony wymagają potwierdzonego zakresu z `{VERIFIED_CERTIFICATIONS}`. Wymóg CVR 19–23 cali stosuje się tylko wtedy, gdy karta tego modelu potwierdza taki zakres; w przeciwnym razie używa się jego rzeczywistych `{AVAILABLE_SIZES}`.
- Wymóg dwóch niezależnych języków realizują osobne zadania PL i EN. Skrót ENG z dokumentu odpowiada EN. Pojedyncza odpowiedź API zawiera tylko zadany język.
- Wszystkie wymagania długości, narracji, wyglądu, fitmentu, zastosowań, słownictwa i stylu pozostają w szablonach. Tytuł i intro mają osobne pola JSON, a treść nie ma nagłówków ani list. Docelowy zakres słów pozostaje konfigurowalny, domyślnie 1200–1800 na język.
- Styl sprzedażowy oznacza opis jakości i efektu wizualnego, zgodnie z dokumentem i specyfikacją: bez bezpośrednich wezwań do zakupu, zwrotów do czytelnika oraz wzmiankowania klientów/dealerów. Frazy o lekkości, technologii, concave lub kompatybilności nie zastępują potwierdzonych faktów i obserwacji.

Nowa instalacja otrzymuje gotowe szablony. Aktualizacja zastępuje tylko pierwotną, nieedytowaną i niepotwierdzoną wersję roboczą, dodając nowy rekord historii. Własne edycje pozostają zachowane. „Przywróć domyślny” tworzy nową wersję aktualnego szablonu z dokumentu.

Importer rozpoznaje dokładną zawartość tego DOCX po SHA256, niezależnie od nazwy pliku. Rozpoznany dokument przywraca te przygotowane szablony; ponowny import bez zmian nie dodaje duplikatów. Inny dokument, nawet nazwany identycznie, zachowuje pełne sekcje i wymaga ręcznego przeglądu oraz potwierdzenia. Zgodność szablonów nie jest potwierdzeniem jakości nieprzeprowadzonego jeszcze rzeczywistego generowania przez AI.
