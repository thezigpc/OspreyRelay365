#!/usr/bin/env pwsh
# Builds Relay365.exe as a framework-dependent single-file executable for Windows x64.
# Requires .NET 8 Desktop Runtime on the target machine (~25 MB vs 200 MB self-contained).
# Output: .\publish\Relay365.exe  +  .\publish\check-runtime.ps1

dotnet publish src\Relay365\Relay365.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=embedded `
  -o publish

if ($LASTEXITCODE -eq 0) {
    # Copy the runtime check script alongside the exe
    if (Test-Path "check-runtime.ps1") {
        Copy-Item "check-runtime.ps1" "publish\check-runtime.ps1" -Force
    }

    $exe = Get-Item "publish\Relay365.exe"
    Write-Host ""
    Write-Host "Published: $($exe.FullName)"
    Write-Host "Size:      $([math]::Round($exe.Length/1MB,1)) MB"
    Write-Host ""
    Write-Host "NOTE: Target machine needs .NET 8 Desktop Runtime."
    Write-Host "      Run check-runtime.ps1 on the target to verify."
} else {
    Write-Error "Publish failed."
}
