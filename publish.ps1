#!/usr/bin/env pwsh
# Builds OspreyRelay365.exe as a framework-dependent single-file executable for Windows x64.
# Requires .NET 10 Desktop Runtime on the target machine.
# Output: .\publish\OspreyRelay365.exe  +  .\publish\check-runtime.ps1

dotnet publish src\OspreyRelay.App\OspreyRelay.App.csproj `
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

    $exe = Get-Item "publish\OspreyRelay365.exe"
    Write-Host ""
    Write-Host "Published: $($exe.FullName)"
    Write-Host "Size:      $([math]::Round($exe.Length/1MB,1)) MB"
    Write-Host ""
    Write-Host "NOTE: Target machine needs .NET 10 Desktop Runtime."
    Write-Host "      Run check-runtime.ps1 on the target to verify."
} else {
    Write-Error "Publish failed."
}
