<#
.SYNOPSIS
    Restores Microsoft Surface power, overlay, and SAM fan settings to factory OEM defaults.
.DESCRIPTION
    This script reads any saved baseline backup created by Surface Fan Control, or applies
    the standard Microsoft Surface defaults:
      - Windows 11 Power Slider Overlay: Recommended / Balanced (00000000-0000-0000-0000-000000000000)
      - Direct SAM Fan RPM Override: 0 RPM (Auto) & Profile 2 (when running as Administrator)
      - Surface Overlay Power Setting: 1 (Balanced AC), 2 (Balanced DC)
      - Processor Max Throttle: 100% (AC & DC)
#>

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Surface Fan Control - Stock Settings Restore Utility   " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

$subgroup = "bfeba2e4-fdcc-41d9-93f4-3f6991c1f730"
$setting  = "a4a0ab02-11f1-4584-bc52-cbb98eabdb0b"
$procSub  = "54533251-82be-4824-96c1-47b60b740d00"
$procMax  = "bc5038f7-23e0-4960-96da-33abaf5935ec"

$backupFile = "$env:LOCALAPPDATA\SurfaceFanControl\stock_settings_backup.json"
$overlayAc = 1
$overlayDc = 2
$throttleAc = 100
$throttleDc = 100
$effectiveOverlayGuid = "00000000-0000-0000-0000-000000000000"

if (Test-Path $backupFile) {
    try {
        $json = Get-Content $backupFile -Raw | ConvertFrom-Json
        if ($json.SurfaceOverlayAcIndex) { $overlayAc = $json.SurfaceOverlayAcIndex }
        if ($json.SurfaceOverlayDcIndex) { $overlayDc = $json.SurfaceOverlayDcIndex }
        if ($json.ProcThrottleMaxAc)     { $throttleAc = $json.ProcThrottleMaxAc }
        if ($json.ProcThrottleMaxDc)     { $throttleDc = $json.ProcThrottleMaxDc }
        if ($json.EffectiveOverlayGuid)  { $effectiveOverlayGuid = $json.EffectiveOverlayGuid }
        Write-Host "[+] Loaded initial baseline backup from: $backupFile" -ForegroundColor Green
    } catch {
        Write-Host "[-] Warning: Could not read backup file, using standard Surface OEM defaults." -ForegroundColor Yellow
    }
} else {
    Write-Host "[*] No custom backup file found; applying standard Surface OEM factory defaults." -ForegroundColor Yellow
}

$nativeCode = @"
using System;
using System.Runtime.InteropServices;

public static class SurfaceRestoreNative
{
    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern uint PowerSetActiveOverlayScheme(ref Guid OverlaySchemeGuid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public static bool ResetSamFan()
    {
        IntPtr h = CreateFileW(@"\\?\ACPI#MSHW0084#2&daba3ff&0#{99bbc337-05d6-4819-b648-39589febb93e}", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
        try
        {
            uint ret;
            byte[] rpmZero = { 0x05, 0x01, 0x01, 0x00, 0x0B, 0x02, 0x00, 0x00, 0x00 };
            byte[] profTwo = { 0x05, 0x01, 0x01, 0x00, 0x0E, 0x01, 0x00, 0x02 };
            DeviceIoControl(h, 0x04001C48, rpmZero, (uint)rpmZero.Length, null, 0, out ret, IntPtr.Zero);
            DeviceIoControl(h, 0x04001C48, profTwo, (uint)profTwo.Length, null, 0, out ret, IntPtr.Zero);
            return true;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
"@

Add-Type -TypeDefinition $nativeCode

Write-Host "[*] Restoring Windows 11 Power Overlay Scheme ($effectiveOverlayGuid)..." -ForegroundColor Gray
$guid = [Guid]::Parse($effectiveOverlayGuid)
[SurfaceRestoreNative]::PowerSetActiveOverlayScheme([ref]$guid) | Out-Null

if ([SurfaceRestoreNative]::ResetSamFan()) {
    Write-Host "[+] Reset direct SAM Fan target RPM to 0 (Auto) and Profile 2." -ForegroundColor Green
}

Write-Host "[*] Applying Surface Overlay Power Setting (AC: $overlayAc, DC: $overlayDc)..." -ForegroundColor Gray
powercfg /setacvalueindex SCHEME_CURRENT $subgroup $setting $overlayAc
powercfg /setdcvalueindex SCHEME_CURRENT $subgroup $setting $overlayDc

Write-Host "[*] Applying Processor Max Throttle (AC: $throttleAc%, DC: $throttleDc%)..." -ForegroundColor Gray
powercfg /setacvalueindex SCHEME_CURRENT $procSub $procMax $throttleAc
powercfg /setdcvalueindex SCHEME_CURRENT $procSub $procMax $throttleDc

Write-Host "[*] Activating power scheme..." -ForegroundColor Gray
powercfg /setactive SCHEME_CURRENT

Write-Host ""
Write-Host "[OK] Successfully restored Surface power, overlay, and fan settings to OEM stock defaults!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
