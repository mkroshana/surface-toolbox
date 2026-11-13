$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "[ERROR] PowerShell 7 (pwsh) is required to run the .NET 8 verification suite." -ForegroundColor Red
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir   = Split-Path -Parent $scriptDir

# Load compiled assembly (prefer UI output directory which includes System.Management.dll and runtime dependencies)
$uiCoreDll = Join-Path $rootDir "src\SurfaceToolbox.UI\bin\Release\net8.0-windows\SurfaceToolbox.Core.dll"
$fallbackCoreDll = Join-Path $rootDir "src\SurfaceToolbox.Core\bin\Release\net8.0-windows\SurfaceToolbox.Core.dll"
if (Test-Path $uiCoreDll) {
    $coreDll = $uiCoreDll
} else {
    $coreDll = $fallbackCoreDll
}

if (-not (Test-Path $coreDll)) {
    Write-Error "Could not find compiled core DLL at: $coreDll"
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Surface Toolbox - Automated Verification Suite     " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

[System.Reflection.Assembly]::LoadFrom($coreDll) | Out-Null
Write-Host "[OK] Successfully loaded SurfaceToolbox.Core assembly." -ForegroundColor Green

# 1. Test Device Detection
Write-Host ""
Write-Host "--- Test 1: Surface Device Detection ---" -ForegroundColor Yellow
$deviceInfo = [SurfaceToolbox.Core.Hardware.SurfaceDeviceDetector]::Detect()
Write-Host "  Manufacturer:               $($deviceInfo.Manufacturer)"
Write-Host "  Model:                      $($deviceInfo.Model)"
Write-Host "  Processor:                  $($deviceInfo.ProcessorName)"
Write-Host "  Is Surface Device:          $($deviceInfo.IsSurfaceDevice)"
Write-Host "  Surface Overlay Supported:  $($deviceInfo.HasSurfaceOverlaySupport)"

if (-not $deviceInfo.HasSurfaceOverlaySupport) {
    Write-Error "Failed: Surface Overlay support is not detected on this system."
}
Write-Host "[OK] Device detection passed." -ForegroundColor Green

$powerManager = $null
$monitor = $null
try {
    # 2. Test Power Manager & Baseline Backup
    Write-Host ""
    Write-Host "--- Test 2: Power Manager & Baseline Backup ---" -ForegroundColor Yellow
    $powerManager = [SurfaceToolbox.Core.Hardware.SurfacePowerManager]::new()
    $backup = $powerManager.StockBackup
    Write-Host "  Captured Baseline Scheme:   $($backup.ActiveSchemeGuid)"
    Write-Host "  Baseline Effective Overlay: $($backup.EffectiveOverlayGuid)"
    Write-Host "  Baseline Overlay AC / DC:   $($backup.SurfaceOverlayAcIndex) / $($backup.SurfaceOverlayDcIndex)"
    Write-Host "  Baseline Throttle AC / DC:  $($backup.ProcThrottleMaxAc)% / $($backup.ProcThrottleMaxDc)%"
    Write-Host "  Direct SAM Available:       $($powerManager.IsDirectSamAvailable)"
    Write-Host "  SMF Telemetry Available:    $($powerManager.SamClient.IsSmfTelemetryAvailable)"

    if ($backup.SurfaceOverlayAcIndex -eq 0) {
        Write-Error "Failed: Invalid baseline overlay AC index (0)."
    }
    Write-Host "[OK] Baseline capture and backup passed." -ForegroundColor Green

    # 3. Test Power Mode Switching (Best Performance -> Quiet -> Stock Restore)
    Write-Host ""
    Write-Host "--- Test 3: Profile Switching & Live SMF Verification ---" -ForegroundColor Yellow
    $monitor = [SurfaceToolbox.Core.Hardware.SurfaceThermalMonitor]::new($powerManager, 10000)

    # Test Best Performance Mode
    Write-Host "  Applying Best Performance Power Mode..."
    $bestProfile = [SurfaceToolbox.Core.Profiles.CoolingProfile]::CreateBestPerformance()
    $okBest = $powerManager.ApplyProfile($bestProfile)
    if (-not $okBest) { Write-Error "Failed: Could not apply Best Performance power mode." }
    Start-Sleep -Milliseconds 350
    $bestSnap = $monitor.CaptureSnapshot()
    Write-Host "  Verified SMF State in Best Performance: FanProfile=$($bestSnap.SmfFanProfile) (Expected: 4), PL1=$($bestSnap.Pl1Watts)W, PL2=$($bestSnap.Pl2Watts)W" -ForegroundColor Green
    if ($bestSnap.SmfFanProfile -ne 4) { Write-Error "Failed: Expected SMF FanProfile 4 in Best Performance, got $($bestSnap.SmfFanProfile)" }

    # Test Quiet Mode
    Write-Host "  Applying Quiet Profile..."
    $quietProfile = [SurfaceToolbox.Core.Profiles.CoolingProfile]::CreateQuiet()
    $okQuiet = $powerManager.ApplyProfile($quietProfile)
    if (-not $okQuiet) { Write-Error "Failed: Could not apply Quiet profile." }
    Start-Sleep -Milliseconds 350
    $quietSnap = $monitor.CaptureSnapshot()
    Write-Host "  Verified SMF State in Quiet: FanProfile=$($quietSnap.SmfFanProfile) (Expected: 2), PL1=$($quietSnap.Pl1Watts)W, PL2=$($quietSnap.Pl2Watts)W" -ForegroundColor Green
    if ($quietSnap.SmfFanProfile -ne 2) { Write-Host "[FAIL] Expected SMF FanProfile=2 in Quiet Mode, got $($quietSnap.SmfFanProfile)" -ForegroundColor Red; exit 1 }

    # Test Stock Restore
    Write-Host "  Testing 1-Click Stock Defaults Restore..."
    $okRestore = $powerManager.RestoreStockSettings()
    if (-not $okRestore) { Write-Error "Failed: Could not restore stock settings." }
    Start-Sleep -Milliseconds 350
    $restoredSnap = $monitor.CaptureSnapshot()
    Write-Host "  Verified Restored SMF State: FanProfile=$($restoredSnap.SmfFanProfile), PL1=$($restoredSnap.Pl1Watts)W, PL2=$($restoredSnap.Pl2Watts)W" -ForegroundColor Green
    if ($powerManager.ActiveTargetRpm -ne 0) { Write-Host "[FAIL] Expected ActiveTargetRpm=0 after stock restore, got $($powerManager.ActiveTargetRpm)" -ForegroundColor Red; exit 1 }
    if ($restoredSnap.SmfFanProfile -ne 2) { Write-Host "[FAIL] Expected SMF FanProfile=2 after stock restore, got $($restoredSnap.SmfFanProfile)" -ForegroundColor Red; exit 1 }
    Write-Host "[OK] Power mode switching and stock restore passed." -ForegroundColor Green

    # 3b. Power mode and fan speed are independent settings
    Write-Host ""
    Write-Host "--- Test 3b: Power Mode and Fan Speed Independence ---" -ForegroundColor Yellow
    if ($powerManager.IsDirectSamAvailable) {
        $overlayBefore = $powerManager.GetEffectiveOverlayGuid()
        if (-not $powerManager.SetManualFanRpm(3000)) { Write-Host "[FAIL] Could not set a fan speed." -ForegroundColor Red; exit 1 }
        if ($powerManager.GetEffectiveOverlayGuid() -ne $overlayBefore) { Write-Host "[FAIL] Setting a fan speed changed the power mode." -ForegroundColor Red; exit 1 }
        $betterProfile = [SurfaceToolbox.Core.Profiles.CoolingProfile]::CreateBetterPerformance()
        if (-not $powerManager.ApplyProfile($betterProfile)) { Write-Host "[FAIL] Could not apply Better Performance power mode." -ForegroundColor Red; exit 1 }
        if ($powerManager.ActiveTargetRpm -ne 3000) { Write-Host "[FAIL] Changing the power mode dropped the fan speed target (got $($powerManager.ActiveTargetRpm))." -ForegroundColor Red; exit 1 }
        if (-not $powerManager.ResetFanSpeedOnly()) { Write-Host "[FAIL] Could not return the fan to automatic control." -ForegroundColor Red; exit 1 }
        if ($powerManager.GetEffectiveOverlayGuid() -ne $betterProfile.OverlaySchemeGuid) { Write-Host "[FAIL] Resetting the fan changed the power mode." -ForegroundColor Red; exit 1 }
        Write-Host "[OK] Fan speed and power mode change independently." -ForegroundColor Green
    } else {
        Write-Host "[SKIP] Direct SAM access unavailable (run as Administrator)." -ForegroundColor Yellow
    }

    # 4. Test Thermal & Fan Telemetry
    Write-Host ""
    Write-Host "--- Test 4: Thermal & Fan Telemetry Polling ---" -ForegroundColor Yellow
    $snapshot = $monitor.CaptureSnapshot()

    $pwrText = if ($snapshot.IsPluggedIn) { "Plugged In (AC)" } else { "Battery (DC)" }
    Write-Host "  Zones Detected:             $($snapshot.Zones.Count)"
    Write-Host "  Peak Temperature:           $($snapshot.PeakTemperatureCelsius) °C"
    Write-Host "  Average Temperature:        $($snapshot.AverageTemperatureCelsius) °C"
    Write-Host "  Live Fan RPM:               $($snapshot.FanRpm) RPM"
    Write-Host "  Active SMF Fan Profile:     $($snapshot.SmfFanProfile)"
    Write-Host "  CPU Power Limits (PL1/PL2): $($snapshot.Pl1Watts)W / $($snapshot.Pl2Watts)W"
    Write-Host "  CPU Frequency:              $($snapshot.CpuFrequencyMhz) MHz"
    Write-Host "  CPU Utilization:            $($snapshot.CpuPerformancePercent) %"
    Write-Host "  Power Source:               $pwrText ($($snapshot.BatteryPercentage)%)"

    Write-Host ""
    Write-Host "  Thermal Zone Sensors:" -ForegroundColor Cyan
    foreach ($zone in $snapshot.Zones) {
        $namePad = $zone.DisplayName.PadRight(25)
        $idPad   = $zone.Id.PadRight(10)
        Write-Host "    $namePad ($idPad): $($zone.TemperatureCelsius) °C"
    }

    if ($snapshot.Zones.Count -eq 0 -or $snapshot.PeakTemperatureCelsius -le 0) {
        Write-Error "Failed: No thermal sensors detected or invalid temperature."
    }
    Write-Host "[OK] Thermal telemetry verification passed." -ForegroundColor Green
} finally {
    if ($null -ne $monitor) { $monitor.Dispose() }
    if ($null -ne $powerManager) { $powerManager.RestoreStockSettings() | Out-Null; $powerManager.Dispose() }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   All Automated Verification Tests PASSED Successfully!  " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
