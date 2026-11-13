<#
.SYNOPSIS
    Checks the toolbox pages' data sources against Windows' own figures.
.DESCRIPTION
    Loads the built Core and UI assemblies and compares what they report with what Windows
    reports through its own tools (powercfg, WMI, Task Scheduler).

    By default nothing on the device is changed: the checks are read-only or work on
    in-memory data and a temporary file.

    -IncludeWrites also exercises per-source power modes and the sign-in task. Those change
    real settings while they run and put every one of them back at the end. They are skipped
    if the app is running or the sign-in task already exists.

    Run from PowerShell 7 (pwsh), elevated, on a Surface, after building Release.
.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_toolbox.ps1
.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_toolbox.ps1 -IncludeWrites
#>
[CmdletBinding()]
param([switch]$IncludeWrites)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "[ERROR] PowerShell 7 (pwsh) is required to load the .NET 8 assemblies." -ForegroundColor Red
    exit 1
}

$rootDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$bin = Join-Path $rootDir "src\SurfaceToolbox.UI\bin\Release\net8.0-windows"
foreach ($dll in "SurfaceToolbox.Core.dll", "SurfaceToolbox.dll") {
    $path = Join-Path $bin $dll
    if (-not (Test-Path $path)) { Write-Host "[ERROR] Not built: $path. Run build.ps1 first." -ForegroundColor Red; exit 1 }
    [System.Reflection.Assembly]::LoadFrom($path) | Out-Null
}

$script:fails = 0
$script:passes = 0
function Check([string]$what, $actual, $expected) {
    if ("$actual" -eq "$expected") { $script:passes++; Write-Host "  [OK]   $what = $actual" -ForegroundColor Green }
    else { $script:fails++; Write-Host "  [FAIL] $what = $actual (expected $expected)" -ForegroundColor Red }
}
function Section([string]$title) { Write-Host ""; Write-Host "--- $title ---" -ForegroundColor Yellow }

$NP  = [SurfaceToolbox.Core.Hardware.NativePower]
$SRC = [SurfaceToolbox.Core.Hardware.PowerSource]
$modeNames = @{ "961cc777-2547-4f9d-8174-7d86181b8a7a" = "BestEfficiency"; "00000000-0000-0000-0000-000000000000" = "Recommended"
                "3af9b8d9-7c97-431d-ad78-34a8bfea439f" = "BetterPerf"; "ded574b5-45a0-4f42-8737-46345c09c238" = "BestPerf" }
function ModeName($g) { $k = "$g".ToLower(); if ($modeNames.ContainsKey($k)) { $modeNames[$k] } else { $k } }
function ModeOf($source) { $g = [Guid]::Empty; $null = $NP::TryGetConfiguredPowerMode($source, [ref]$g); ModeName $g }
function Caps {
    $m = powercfg /query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX | Select-String 'Current (AC|DC) Power Setting Index: (0x[0-9a-fA-F]+)'
    ($m | ForEach-Object { [Convert]::ToInt32($_.Matches[0].Groups[2].Value, 16) }) -join '/'
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Surface Toolbox - Toolbox Checks                   " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Battery -----------------------------------------------------------------------------------------
Section "Battery: the reader against Windows' battery report"
$battery = [SurfaceToolbox.Core.Hardware.SurfaceBatteryClient]::new().Read()
if ($null -eq $battery) {
    Check "battery found" $false $true
} else {
    $xmlPath = Join-Path $env:TEMP "sfc-toolbox-battery-$PID.xml"
    $null = & powercfg /batteryreport /xml /output $xmlPath 2>&1
    [xml]$report = Get-Content $xmlPath -Raw; Remove-Item $xmlPath -Force
    $b = $report.BatteryReport.Batteries.Battery | Select-Object -First 1
    $status = Get-CimInstance -Namespace root\WMI -ClassName BatteryStatus | Select-Object -First 1
    Check "design capacity Wh" $battery.DesignWh ($b.DesignCapacity / 1000.0)
    Check "full charge capacity Wh" $battery.FullChargeWh ($b.FullChargeCapacity / 1000.0)
    Check "cycle count" $battery.CycleCount $b.CycleCount
    Check "remaining Wh within 0.3" ([math]::Abs($battery.RemainingWh - $status.RemainingCapacity / 1000.0) -le 0.3) $true
    Check "voltage V within 0.2" ([math]::Abs($battery.VoltageV - $status.Voltage / 1000.0) -le 0.2) $true
    Check "on AC" $battery.IsOnAcPower $status.PowerOnline
    Check "health percent" ([math]::Round($battery.HealthPercent, 1)) ([math]::Round(100.0 * $b.FullChargeCapacity / $b.DesignCapacity, 1))
}

# 2. Device ------------------------------------------------------------------------------------------
Section "Device: the report against WMI and the storage cmdlets"
$device = [SurfaceToolbox.Core.Hardware.SurfaceDeviceReportReader]::new().Read()
$cs = Get-CimInstance Win32_ComputerSystem; $os = Get-CimInstance Win32_OperatingSystem; $bios = Get-CimInstance Win32_BIOS
Check "model" $device.Model $cs.Model
Check "memory GB" $device.MemoryGb ([math]::Round($cs.TotalPhysicalMemory / 1GB, 1))
Check "Windows build" $device.WindowsBuild $os.BuildNumber
Check "UEFI version" $device.UefiVersion $bios.SMBIOSBIOSVersion
$fw = @(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='FIRMWARE'" | Where-Object { $_.DeviceName -and $_.DriverVersion })
Check "firmware component count" $device.Firmware.Count $fw.Count
$disk = Get-PhysicalDisk | Select-Object -First 1
if ($disk -and $device.Drives.Count -gt 0) {
    $counter = $disk | Get-StorageReliabilityCounter
    Check "drive model" $device.Drives[0].Model $disk.FriendlyName
    Check "drive kind" $device.Drives[0].Kind "$($disk.BusType) $($disk.MediaType)"
    Check "drive health" $device.Drives[0].Health $disk.HealthStatus
    Check "drive life used" $device.Drives[0].WearPercent $counter.Wear
} else { Check "a drive was reported" ($device.Drives.Count -gt 0) $true }

# 3. History -----------------------------------------------------------------------------------------
Section "History: buffer, downsampling and CSV"
$H = [SurfaceToolbox.Core.Models.TelemetryHistory]; $S = [SurfaceToolbox.Core.Models.TelemetrySample]
$t0 = [DateTime]::new(2026, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
$history = $H::new(10)
0..24 | ForEach-Object { $history.Add($S::new($t0.AddSeconds($_), 40 + $_ * 0.1, 35.0, 3000 + $_, [double]($_ % 100), 2000.0, ($_ % 2 -eq 0), 80)) }
Check "count is capped at capacity" $history.Count 10
Check "oldest overwritten, order kept" (($history.Snapshot() | ForEach-Object FanRpm) -join ',') "3015,3016,3017,3018,3019,3020,3021,3022,3023,3024"
Check "window of 4 s" (($history.Snapshot([TimeSpan]::FromSeconds(4)) | ForEach-Object FanRpm) -join ',') "3020,3021,3022,3023,3024"
Check "12 values into 4 buckets" (($H::Downsample([double[]](1..12), 4)) -join ',') "2,5,8,11"
$small = $H::new(5); 0..1 | ForEach-Object { $small.Add($S::new($t0.AddSeconds($_), 40 + $_ * 0.1, 35.0, 3000 + $_, [double]$_, 2000.0, $true, 80)) }
$writer = [System.IO.StringWriter]::new(); $null = $small.WriteCsv($writer); $csvLines = $writer.ToString().TrimEnd() -split "`r?`n"
Check "CSV header" $csvLines[0] "time_utc,peak_temp_c,average_temp_c,fan_rpm,cpu_load_percent,cpu_mhz,plugged_in,battery_percent"
Check "CSV row" $csvLines[2] "2026-01-01T00:00:01Z,40.1,35.0,3001,1,2000,1,80"
$previousCulture = [System.Threading.Thread]::CurrentThread.CurrentCulture
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo("de-DE")
$writer2 = [System.IO.StringWriter]::new(); $null = $small.WriteCsv($writer2)
[System.Threading.Thread]::CurrentThread.CurrentCulture = $previousCulture
Check "CSV is the same under a comma-decimal culture" ($writer2.ToString() -eq $writer.ToString()) $true

$historyVm = [SurfaceToolbox.UI.ViewModels.HistoryViewModel]::new(); $historyVm.SetVisible($true)
$start = [DateTime]::UtcNow.AddMinutes(-10)
0..599 | ForEach-Object {
    $snap = [SurfaceToolbox.Core.Models.SystemThermalSnapshot]::new()
    $snap.Timestamp = $start.AddSeconds($_); $snap.PeakTemperatureCelsius = 40 + $_ * 0.01; $snap.FanRpm = $(if ($_ -lt 300) { 0 } else { 3000 }); $snap.CpuPerformancePercent = 25
    $historyVm.Record($snap)
}
Check "5 minute window" $historyVm.CoverageText "5 min 00 s recorded of the last 5 min, 301 readings"
$historyVm.IsWindow15 = $true
Check "15 minute window" $historyVm.CoverageText "9 min 59 s recorded of the last 15 min, 600 readings"
Check "600 readings drawn as 400 points" $historyVm.TemperatureValues.Count 400
$exportPath = Join-Path $env:TEMP "sfc-toolbox-history-$PID.csv"
Check "export to file" $historyVm.ExportCsv($exportPath) $true
Check "exported lines" @(Get-Content $exportPath).Count 601
Remove-Item $exportPath -Force

# 4. Power modes (read-only) -------------------------------------------------------------------------
Section "Power modes: per-source values against the registry"
$reg = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes'
Check "plugged-in mode" (ModeOf $SRC::PluggedIn) (ModeName $reg.ActiveOverlayAcPowerScheme)
Check "on-battery mode" (ModeOf $SRC::Battery) (ModeName $reg.ActiveOverlayDcPowerScheme)

# 5. Sign-in task definition (not registered) --------------------------------------------------------
Section "Sign-in task: the definition the app would register"
$Autostart = [SurfaceToolbox.UI.Services.AutostartService]
$definition = $Autostart::BuildDefinition("C:\Some Folder\SurfaceToolbox.exe")
$ns = "http://schemas.microsoft.com/windows/2004/02/mit/task"
function TaskValue([string[]]$path) { $e = $definition.Root; foreach ($p in $path) { $e = $e.Element([System.Xml.Linq.XName]::Get($p, $ns)) }; $e.Value }
Check "starts on battery" (TaskValue "Settings", "DisallowStartIfOnBatteries") "false"
Check "keeps running on battery" (TaskValue "Settings", "StopIfGoingOnBatteries") "false"
Check "no time limit" (TaskValue "Settings", "ExecutionTimeLimit") "PT0S"
Check "highest privileges" (TaskValue "Principals", "Principal", "RunLevel") "HighestAvailable"
Check "program is quoted" (TaskValue "Actions", "Exec", "Command") '"C:\Some Folder\SurfaceToolbox.exe"'
Check "starts in the tray" (TaskValue "Actions", "Exec", "Arguments") "--tray"

# 6. Checks that change settings and put them back ---------------------------------------------------
if ($IncludeWrites) {
    $appRunning = [bool](Get-Process -Name "SurfaceToolbox" -ErrorAction SilentlyContinue)
    $taskName = $Autostart::TaskName
    $taskExists = [bool](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)

    Section "Per-source power modes (changes settings, then restores them)"
    if ($appRunning) {
        Write-Host "  [SKIP] The app is running; close it first." -ForegroundColor Yellow
    } else {
        $CP = [SurfaceToolbox.Core.Profiles.CoolingProfile]
        $sessionFile = "$env:LOCALAPPDATA\SurfaceToolbox\session_baseline.json"
        $origAc = [Guid]"$($reg.ActiveOverlayAcPowerScheme)"; $origDc = [Guid]"$($reg.ActiveOverlayDcPowerScheme)"; $origCaps = Caps
        $manager = [SurfaceToolbox.Core.Hardware.SurfacePowerManager]::new()
        try {
            $inUse = $manager.CurrentPowerSource
            $other = if ($inUse -eq $SRC::PluggedIn) { $SRC::Battery } else { $SRC::PluggedIn }
            $inUseBefore = ModeOf $inUse
            Check "apply Quiet to the source not in use ($other)" ($manager.ApplyProfile($CP::CreateQuiet(), $other)) $true
            Check "  its mode" (ModeOf $other) "BestEfficiency"
            Check "  the source in use is untouched" (ModeOf $inUse) $inUseBefore
            Check "apply Better Performance to the source in use ($inUse)" ($manager.ApplyProfile($CP::CreateBetterPerformance(), $inUse)) $true
            Check "  its mode" (ModeOf $inUse) "BetterPerf"
            Check "  the other source is untouched" (ModeOf $other) "BestEfficiency"
            Check "a fan speed leaves the power mode alone" "$($manager.SetManualFanRpm(3000)) $(ModeOf $inUse)" "True BetterPerf"
            Check "a power mode leaves the fan target alone" "$($manager.ApplyProfile($CP::CreateBalanced(), $inUse)) $($manager.ActiveTargetRpm)" "True 3000"
            $manager.Dispose(); $manager = $null
            Check "exit restores the plugged-in mode" (ModeOf $SRC::PluggedIn) (ModeName $origAc)
            Check "exit restores the on-battery mode" (ModeOf $SRC::Battery) (ModeName $origDc)
            Check "exit restores the processor caps" (Caps) $origCaps
            Check "no baseline file left behind" (Test-Path $sessionFile) $false
        } finally {
            if ($manager) { $manager.Dispose() }
            Remove-Item $sessionFile -ErrorAction SilentlyContinue
            $null = $NP::SetConfiguredPowerMode($SRC::PluggedIn, $origAc); $null = $NP::SetConfiguredPowerMode($SRC::Battery, $origDc)
        }
    }

    Section "Sign-in task (registers it, checks it, removes it)"
    if ($taskExists) {
        Write-Host "  [SKIP] '$taskName' already exists on this device; leaving it alone." -ForegroundColor Yellow
    } else {
        $service = $Autostart::new(); $exe = Join-Path $bin "SurfaceToolbox.exe"; $err = ""
        try {
            Check "Enable" $service.Enable($exe, [ref]$err) $true
            $task = Get-ScheduledTask -TaskName $taskName
            Check "registered program" $task.Actions[0].Execute.Trim('"') $exe
            Check "registered to start on battery" $task.Settings.DisallowStartIfOnBatteries $false
            Check "read back by the app" $service.GetRegisteredCommand() $exe
            Check "Disable" $service.Disable([ref]$err) $true
            Check "task removed" ([bool](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) $false
        } finally {
            if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
        }
    }
} else {
    Write-Host ""
    Write-Host "(Skipped the checks that change settings. Add -IncludeWrites to run them; they restore everything.)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
if ($script:fails -eq 0) {
    Write-Host "   All $($script:passes) toolbox checks passed.                       " -ForegroundColor Green
} else {
    Write-Host "   $($script:fails) of $($script:passes + $script:fails) toolbox checks FAILED.            " -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
exit $(if ($script:fails -eq 0) { 0 } else { 1 })
