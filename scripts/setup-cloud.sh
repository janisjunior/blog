#!/usr/bin/env bash
set -euo pipefail
cd /workspace/blog
export DOTNET_ROOT=/workspace/.dotnet
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:$PATH"
if ! test -x "$DOTNET_ROOT/dotnet" || ! "$DOTNET_ROOT/dotnet" --list-sdks | rg -q '^10\.0\.401 '; then
  python3 - <<'PY'
import hashlib, json, pathlib, tarfile, urllib.request
version = '10.0.401'
with urllib.request.urlopen('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json', timeout=60) as response:
    releases = json.load(response)['releases']
sdks = [sdk for release in releases for sdk in release.get('sdks', [release['sdk']])]
sdk = next(sdk for sdk in sdks if sdk['version'] == version)
artifact = next(f for f in sdk['files'] if f['rid'] == 'linux-x64' and f['name'].endswith('.tar.gz'))
archive = pathlib.Path('/tmp/wcm-dotnet-sdk.tar.gz')
if not archive.exists() or hashlib.file_digest(archive.open('rb'), 'sha512').hexdigest().lower() != artifact['hash'].lower():
    urllib.request.urlretrieve(artifact['url'], archive)
with archive.open('rb') as stream:
    if hashlib.file_digest(stream, 'sha512').hexdigest().lower() != artifact['hash'].lower():
        raise RuntimeError('Błędna suma SHA512 oficjalnego SDK .NET.')
root = pathlib.Path('/workspace/.dotnet'); root.mkdir(exist_ok=True)
with tarfile.open(archive) as tar:
    tar.extractall(root, filter='data')
PY
fi
dotnet restore WheelContentManager.sln --locked-mode
dotnet build WheelContentManager.sln --no-restore
dotnet test tests/WheelContentManager.Tests --no-build --no-restore
