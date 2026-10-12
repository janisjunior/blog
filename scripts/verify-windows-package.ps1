$ErrorActionPreference = 'Stop'
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$reportPath = Join-Path $artifactRoot 'Security-report.txt'
$report = [System.Collections.Generic.List[string]]::new()
$report.Add("Verification started (UTC): $([DateTime]::UtcNow.ToString('o'))")
try {
    $packages = @('WT-Blog-Generator-Setup.exe', 'WT-Blog-Generator-win-x64.zip')
    foreach ($name in $packages) {
        if (!(Test-Path (Join-Path $artifactRoot $name) -PathType Leaf)) { throw "Missing package: $name" }
    }
    $status = Get-MpComputerStatus
    if (!$status.AMServiceEnabled -or !$status.AntivirusEnabled) {
        throw 'Microsoft Defender antivirus is unavailable or disabled. Packages will not be uploaded.'
    }
    Update-MpSignature
    $status = Get-MpComputerStatus
    $report.Add("Defender engine: $($status.AMEngineVersion)")
    $report.Add("Defender signatures: $($status.AntivirusSignatureVersion)")
    $report.Add("Signature updated (UTC): $($status.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o'))")
    # Scan the unpacked application, installer and portable ZIP before distribution.
    Start-MpScan -ScanType CustomScan -ScanPath $artifactRoot
    $detections = @(Get-MpThreatDetection | Where-Object {
        @($_.Resources | Where-Object {
            $_.IndexOf($artifactRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -gt 0
    })
    if ($detections.Count -gt 0) {
        $report.Add(($detections | Select-Object ThreatID, InitialDetectionTime, Resources | Format-List | Out-String))
        throw 'Defender reported a threat in the build artifacts. Packages will not be uploaded.'
    }
    $checksums = foreach ($name in $packages) {
        $path = Join-Path $artifactRoot $name
        if (!(Test-Path $path -PathType Leaf)) { throw "Package disappeared during scan: $name" }
        $hash = Get-FileHash -Path $path -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $name"
    }
    $checksums | Set-Content (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding ascii
    foreach ($relativePath in @('WT-Blog-Generator-Setup.exe', 'WheelContentManager/WheelContentManager.Desktop.exe', 'WheelContentManager/Worker/WheelContentManager.Worker.exe')) {
        $signature = Get-AuthenticodeSignature (Join-Path $artifactRoot $relativePath)
        $report.Add("Authenticode ${relativePath}: $($signature.Status)")
    }
    $report.Add('Result: Defender scan completed; no threats reported in these build artifacts.')
    $report.Add('This is one antivirus scan, not a guarantee of safety or a Chrome reputation check. Executables may be unsigned; do not bypass browser or antivirus blocks on this basis.')
} catch {
    $report.Add("Result: FAILED - $($_.Exception.Message)")
    $details = ($report -join "`n").Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Host "::error::$details"
    throw
} finally {
    $report | Set-Content $reportPath -Encoding utf8
}
