[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$RestoreStock,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($RestoreStock) {
    Write-Host "[*] Executing stock settings restore script..." -ForegroundColor Cyan
    & "$scriptDir\scripts\restore_stock_settings.ps1"
    exit 0
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         Surface Fan Control - Build Script               " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

# Locate dotnet CLI with SDK
if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\sdk") {
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    $dotnetCmd = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
} elseif (Test-Path "C:\Program Files\dotnet\sdk") {
    $env:DOTNET_ROOT = "C:\Program Files\dotnet"
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    $dotnetCmd = "C:\Program Files\dotnet\dotnet.exe"
} else {
    $dotnetCmd = "dotnet"
}

Write-Host "[*] Using dotnet: $dotnetCmd" -ForegroundColor Gray
& $dotnetCmd --version

$slnPath = Join-Path $scriptDir "src\SurfaceFanControl.sln"
Write-Host "[*] Building Solution ($Configuration): $slnPath..." -ForegroundColor Cyan
& $dotnetCmd build $slnPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "[OK] Build completed successfully!" -ForegroundColor Green

if ($Run) {
    $exePath = Join-Path $scriptDir "src\SurfaceFanControl.UI\bin\$Configuration\net8.0-windows\SurfaceFanControl.UI.exe"
    if (Test-Path $exePath) {
        Write-Host "[*] Launching Surface Fan Control: $exePath..." -ForegroundColor Green
        Start-Process -FilePath $exePath
    } else {
        Write-Error "Could not find built executable at: $exePath"
    }
}
