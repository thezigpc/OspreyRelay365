#!/usr/bin/env pwsh
# Checks whether the .NET 8 Desktop Runtime is installed on this machine.
# Run this on the target PC before deploying 365 Relay.

$required = [version]"8.0"
$downloadUrl = "https://dotnet.microsoft.com/download/dotnet/8.0"

Write-Host ""
Write-Host "365 Relay — Runtime Check" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────"

# List installed .NET runtimes via dotnet CLI
$installed = $null
try {
    $runtimes = & dotnet --list-runtimes 2>$null
    $installed = $runtimes |
        Where-Object { $_ -match "^Microsoft\.WindowsDesktop\.App\s+(\d+\.\d+)" } |
        ForEach-Object { [version]($Matches[1]) } |
        Where-Object { $_ -ge $required } |
        Sort-Object -Descending |
        Select-Object -First 1
} catch {
    # dotnet not found at all
}

if ($installed) {
    Write-Host ""
    Write-Host "  OK  .NET $installed Desktop Runtime is installed." -ForegroundColor Green
    Write-Host ""
    Write-Host "  365 Relay should run on this machine."
} else {
    Write-Host ""
    Write-Host "  MISSING  .NET 8 Desktop Runtime was not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Please download and install it from:" -ForegroundColor Yellow
    Write-Host "  $downloadUrl" -ForegroundColor Yellow
    Write-Host ""

    $answer = Read-Host "  Open the download page now? [Y/N]"
    if ($answer -match "^[Yy]") {
        Start-Process $downloadUrl
    }
}

Write-Host ""
