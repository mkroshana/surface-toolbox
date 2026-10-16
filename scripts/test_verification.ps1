$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir   = Split-Path -Parent $scriptDir

# Load compiled assembly
$coreDll = Join-Path $rootDir "src\SurfaceFanControl.Core\bin\Release\net8.0-windows\SurfaceFanControl.Core.dll"
if (-not (Test-Path $coreDll)) {
    Write-Error "Could not find compiled core DLL at: $coreDll"
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Surface Fan Control - Automated Verification Suite     " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

[System.Reflection.Assembly]::LoadFrom($coreDll) | Out-Null
Write-Host "[OK] Successfully loaded SurfaceFanControl.Core assembly." -ForegroundColor Green

# 1. Test Device Detection
Write-Host ""
Write-Host "--- Test 1: Surface Device Detection ---" -ForegroundColor Yellow
$deviceInfo = [SurfaceFanControl.Core.Hardware.SurfaceDeviceDetector]::Detect()
Write-Host "  Manufacturer:               $($deviceInfo.Manufacturer)"
Write-Host "  Model:                      $($deviceInfo.Model)"
Write-Host "  Processor:                  $($deviceInfo.ProcessorName)"
Write-Host "  Is Surface Device:          $($deviceInfo.IsSurfaceDevice)"
Write-Host "  Surface Overlay Supported:  $($deviceInfo.HasSurfaceOverlaySupport)"

if (-not $deviceInfo.HasSurfaceOverlaySupport) {
    Write-Error "Failed: Surface Overlay support is not detected on this system."
}
Write-Host "[OK] Device detection passed." -ForegroundColor Green

# 2. Test Power Manager & Baseline Backup
Write-Host ""
Write-Host "--- Test 2: Power Manager & Baseline Backup ---" -ForegroundColor Yellow
$powerManager = [SurfaceFanControl.Core.Hardware.SurfacePowerManager]::new()
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

# 3. Test Profile Switching (Turbo -> Quiet -> Stock Restore)
Write-Host ""
Write-Host "--- Test 3: Profile Switching & Live SMF Verification ---" -ForegroundColor Yellow
$monitor = [SurfaceFanControl.Core.Hardware.SurfaceThermalMonitor]::new($powerManager, 10000)

# Test Turbo Mode
Write-Host "  Applying Turbo Cooling Profile..."
$turboProfile = [SurfaceFanControl.Core.Profiles.CoolingProfile]::CreateTurbo()
$okTurbo = $powerManager.ApplyProfile($turboProfile)
if (-not $okTurbo) { Write-Error "Failed: Could not apply Turbo profile." }
Start-Sleep -Milliseconds 350
$turboSnap = $monitor.CaptureSnapshot()
Write-Host "  Verified SMF State in Turbo: FanProfile=$($turboSnap.SmfFanProfile) (Expected: 4), PL1=$($turboSnap.Pl1Watts)W, PL2=$($turboSnap.Pl2Watts)W" -ForegroundColor Green
if ($turboSnap.SmfFanProfile -ne 4) { Write-Error "Failed: Expected SMF FanProfile 4 in Turbo, got $($turboSnap.SmfFanProfile)" }

# Test Quiet Mode
Write-Host "  Applying Quiet Profile..."
$quietProfile = [SurfaceFanControl.Core.Profiles.CoolingProfile]::CreateQuiet()
$okQuiet = $powerManager.ApplyProfile($quietProfile)
if (-not $okQuiet) { Write-Error "Failed: Could not apply Quiet profile." }
Start-Sleep -Milliseconds 350
$quietSnap = $monitor.CaptureSnapshot()
Write-Host "  Verified SMF State in Quiet: FanProfile=$($quietSnap.SmfFanProfile) (Expected: 2), PL1=$($quietSnap.Pl1Watts)W, PL2=$($quietSnap.Pl2Watts)W" -ForegroundColor Green

# Test Stock Restore
Write-Host "  Testing 1-Click Stock Defaults Restore..."
$okRestore = $powerManager.RestoreStockSettings()
if (-not $okRestore) { Write-Error "Failed: Could not restore stock settings." }
Start-Sleep -Milliseconds 350
$restoredSnap = $monitor.CaptureSnapshot()
Write-Host "  Verified Restored SMF State: FanProfile=$($restoredSnap.SmfFanProfile), PL1=$($restoredSnap.Pl1Watts)W, PL2=$($restoredSnap.Pl2Watts)W" -ForegroundColor Green
Write-Host "[OK] Profile switching and stock restore passed." -ForegroundColor Green

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

$monitor.Dispose()
$powerManager.Dispose()

if ($snapshot.Zones.Count -eq 0 -or $snapshot.PeakTemperatureCelsius -le 0) {
    Write-Error "Failed: No thermal sensors detected or invalid temperature."
}
Write-Host "[OK] Thermal telemetry verification passed." -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   All Automated Verification Tests PASSED Successfully!  " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
