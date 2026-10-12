param([ValidateSet('win-x64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Polecenie dotnet nie powiodło się ($LASTEXITCODE)." }
}
Invoke-DotNet -Arguments @('restore', 'WheelContentManager.sln', '--locked-mode')
Invoke-DotNet -Arguments @('build', 'WheelContentManager.sln', '-c', 'Release', '--no-restore')
$testResults = Join-Path (Get-Location) 'artifacts/test-results'
try {
    Invoke-DotNet -Arguments @('test', 'tests/WheelContentManager.Tests', '-c', 'Release', '--no-build', '--logger', 'trx;LogFileName=windows.trx', '--results-directory', $testResults)
} catch {
    $report = Join-Path $testResults 'windows.trx'
    if (Test-Path $report) {
        [xml]$results = Get-Content $report -Raw
        foreach ($test in $results.TestRun.Results.UnitTestResult) {
            if ($test.outcome -eq 'Failed') {
                $message = "$($test.testName): $($test.Output.ErrorInfo.Message)".Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
                Write-Host "::error::$message"
            }
        }
    }
    throw
}
$destination = Join-Path (Get-Location) 'artifacts/WheelContentManager'
$worker = Join-Path (Get-Location) 'artifacts/worker'
if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
if (Test-Path $worker) { Remove-Item -Recurse -Force $worker }
Invoke-DotNet -Arguments @('publish', 'src/WheelContentManager.Desktop', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-p:RestoreLockedMode=true', '-p:PublishSingleFile=false', '-o', $destination)
Invoke-DotNet -Arguments @('publish', 'src/WheelContentManager.Worker', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-p:RestoreLockedMode=true', '-p:PublishSingleFile=false', '-o', $worker)
$workerDestination = Join-Path $destination 'Worker'
New-Item -ItemType Directory -Force $workerDestination | Out-Null
Copy-Item -Recurse "$worker/*" $workerDestination
Copy-Item 'README.md' $destination
Copy-Item -Recurse 'docs' $destination
$zip = Join-Path (Get-Location) "artifacts/WT-Blog-Generator-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$destination/*" -DestinationPath $zip
Write-Host "Gotowe: $zip"
Write-Host 'Uruchom WheelContentManager.Desktop.exe na Windows 11. Nie przenoś samego pliku EXE bez pozostałych plików paczki.'
