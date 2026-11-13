[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$RestoreStock,
    [switch]$Pull,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Resolve-DotNet {
    $dotnetLocalDir = "$env:LOCALAPPDATA\Microsoft\dotnet"
    $dotnetSystemDir = "C:\Program Files\dotnet"

    if (Test-Path (Join-Path $dotnetLocalDir "dotnet.exe")) {
        $env:DOTNET_ROOT = $dotnetLocalDir
        if ($env:PATH -notlike "*$dotnetLocalDir*") {
            $env:PATH = "$dotnetLocalDir;$env:PATH"
        }
        return (Join-Path $dotnetLocalDir "dotnet.exe")
    } elseif (Test-Path (Join-Path $dotnetSystemDir "dotnet.exe")) {
        $env:DOTNET_ROOT = $dotnetSystemDir
        if ($env:PATH -notlike "*$dotnetSystemDir*") {
            $env:PATH = "$dotnetSystemDir;$env:PATH"
        }
        return (Join-Path $dotnetSystemDir "dotnet.exe")
    } else {
        return "dotnet"
    }
}

if ($RestoreStock) {
    if (Get-Process -Name "SurfaceToolbox" -ErrorAction SilentlyContinue) {
        Write-Host "[*] Stopping running SurfaceToolbox instance..." -ForegroundColor Yellow
        Stop-Process -Name "SurfaceToolbox" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }
    Write-Host "[*] Executing stock settings restore script..." -ForegroundColor Cyan
    & "$scriptDir\scripts\restore_stock_settings.ps1"
    exit 0
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         Surface Toolbox - Build Script               " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

if ($Pull) {
    # Get the latest changes before building. A pull that fails stops here, so a running copy of the app is left alone.
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error "-Pull needs git, and git was not found on the PATH."
        exit 1
    }

    $scriptHashBefore = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    Write-Host "[*] Getting the latest changes (git pull)..." -ForegroundColor Cyan

    # git reports its progress on the error stream, which must not count as a failure: only its exit code does.
    $ErrorActionPreference = "Continue"
    & git -C $scriptDir pull --ff-only
    $pullExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    if ($pullExitCode -ne 0) {
        Write-Error "git pull failed with exit code $pullExitCode. Nothing was built."
        exit $pullExitCode
    }

    if ((Get-FileHash -LiteralPath $PSCommandPath).Hash -ne $scriptHashBefore) {
        # The pull replaced this script while its old copy is still the one running: hand over to the new copy.
        Write-Host "[*] The build script itself was updated. Restarting it..." -ForegroundColor Yellow
        & $PSCommandPath -Run:$Run -Configuration $Configuration
        exit $LASTEXITCODE
    }
}

if (Get-Process -Name "SurfaceToolbox" -ErrorAction SilentlyContinue) {
    Write-Host "[*] Stopping running SurfaceToolbox instance..." -ForegroundColor Yellow
    Stop-Process -Name "SurfaceToolbox" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

# Locate dotnet CLI with SDK
$dotnetExe = Resolve-DotNet

Write-Host "[*] Using dotnet: $dotnetExe" -ForegroundColor Gray
& $dotnetExe --version

$slnPath = Join-Path $scriptDir "src\SurfaceToolbox.sln"
Write-Host "[*] Building Solution ($Configuration): $slnPath..." -ForegroundColor Cyan
& $dotnetExe build $slnPath -c $Configuration -p:Platform="Any CPU"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "[OK] Build completed successfully!" -ForegroundColor Green

if ($Run) {
    $exePath = Join-Path $scriptDir "src\SurfaceToolbox.UI\bin\$Configuration\net8.0-windows\SurfaceToolbox.exe"
    if (Test-Path $exePath) {
        Write-Host "[*] Launching Surface Toolbox: $exePath..." -ForegroundColor Green
        Start-Process -FilePath $exePath
    } else {
        Write-Error "Could not find built executable at: $exePath"
    }
}
