# Blog

## Środowisko deweloperskie Codex

Repozytorium jest przygotowane do pracy w środowisku chmurowym Codex.
Kopia robocza znajduje się w `/workspace/blog`.

Każde zadanie działa w osobnym środowisku. Korzystaj z istniejącej kopii
repozytorium; nie twórz dodatkowego Git worktree bez wyraźnej prośby.

Stan sprawdzony podczas konfiguracji:

- dostęp do repozytorium na GitHub działa przez uwierzytelnianie platformy;
- dostępne narzędzia: Git, Node.js 24.19.0, npm 11.9.0 i Python 3.12.14;
- projekt nie wymaga obecnie instalowania zależności ani dodatkowych sekretów.

Kontrola kopii roboczej i połączenia z GitHub:

```sh
cd /workspace/blog
git status --short
git ls-remote origin HEAD refs/heads/main
```

Repozytorium nie zawiera jeszcze aplikacji bloga, manifestu zależności,
skryptu uruchamiania ani testów. Nie ma zatem serwera do uruchomienia.
Po dodaniu aplikacji należy uzupełnić konfigurację środowiska o instalację
zależności oraz instrukcję uruchamiania i sprawdzić działanie aplikacji.

Konfiguracja i publikacja środowiska Codex odbywają się w jego ustawieniach.
Wysłanie plików na GitHub nie publikuje środowiska ani strony internetowej.
