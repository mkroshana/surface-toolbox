<#
.SYNOPSIS
    Restores Microsoft Surface power, overlay, and SAM fan settings to factory OEM defaults.
.DESCRIPTION
    This script reads any saved baseline backup created by Surface Toolbox, or applies
    the standard Microsoft Surface defaults:
      - Windows 11 Power Slider Overlay: Recommended / Balanced (00000000-0000-0000-0000-000000000000)
      - Direct SAM Fan RPM Override: 0 RPM (Auto) & Profile 2 (when running as Administrator)
      - Surface Overlay Power Setting: 1 (Balanced AC), 2 (Balanced DC)
      - Processor Max Throttle: 100% (AC & DC)
#>

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Surface Toolbox - Stock Settings Restore Utility   " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

$subgroup = "bfeba2e4-fdcc-41d9-93f4-3f6991c1f730"
$setting  = "a4a0ab02-11f1-4584-bc52-cbb98eabdb0b"
$procSub  = "54533251-82be-4824-96c1-47b60b740d00"
$procMax  = "bc5038f7-23e0-4960-96da-33abaf5935ec"
$procMin  = "893dee8e-2bef-41e0-89c6-b55d0929964c"

$runningApp = Get-Process -Name "SurfaceToolbox" -ErrorAction SilentlyContinue
if ($null -ne $runningApp) {
    Write-Host "[*] Stopping running SurfaceToolbox instance to prevent keep-alive override..." -ForegroundColor Yellow
    try {
        Stop-Process -Name "SurfaceToolbox" -Force -ErrorAction Stop
        Start-Sleep -Milliseconds 300
        Write-Host "[+] Stopped running SurfaceToolbox process." -ForegroundColor Green
    } catch {
        Write-Host "[WARN] Could not stop running SurfaceToolbox process (it may be running as Administrator). Run this script as Administrator to stop it." -ForegroundColor Yellow
    }
}

$backupFile = "$env:LOCALAPPDATA\SurfaceToolbox\stock_settings_backup.json"
if (-not (Test-Path $backupFile)) {
    # The app was called Surface Fan Control before; its backup stays in the old folder until the renamed app has run once.
    $legacyBackup = Join-Path $env:LOCALAPPDATA ("Surface" + "FanControl\stock_settings_backup.json")
    if (Test-Path $legacyBackup) { $backupFile = $legacyBackup }
}
$activeSchemeGuid = "SCHEME_CURRENT"
$overlayAc = 1
$overlayDc = 2
$throttleAc = 100
$throttleDc = 100
$throttleMinAc = $null
$throttleMinDc = $null
$effectiveOverlayGuid = "00000000-0000-0000-0000-000000000000"

if (Test-Path $backupFile) {
    try {
        $json = Get-Content $backupFile -Raw | ConvertFrom-Json
        if ($json.ActiveSchemeGuid) {
            $parsedScheme = [Guid]::Empty
            if ([Guid]::TryParse([string]$json.ActiveSchemeGuid, [ref]$parsedScheme) -and $parsedScheme -ne [Guid]::Empty) {
                $activeSchemeGuid = $parsedScheme.ToString()
            }
        }
        if ($null -ne $json.SurfaceOverlayAcIndex -and $json.SurfaceOverlayAcIndex -gt 0) { $overlayAc = $json.SurfaceOverlayAcIndex }
        if ($null -ne $json.SurfaceOverlayDcIndex -and $json.SurfaceOverlayDcIndex -gt 0) { $overlayDc = $json.SurfaceOverlayDcIndex }
        if ($null -ne $json.ProcThrottleMaxAc -and $json.ProcThrottleMaxAc -gt 0)         { $throttleAc = $json.ProcThrottleMaxAc }
        if ($null -ne $json.ProcThrottleMaxDc -and $json.ProcThrottleMaxDc -gt 0)         { $throttleDc = $json.ProcThrottleMaxDc }
        if ($null -ne $json.ProcThrottleMinAc -and $json.ProcThrottleMinAc -ge 0)         { $throttleMinAc = $json.ProcThrottleMinAc }
        if ($null -ne $json.ProcThrottleMinDc -and $json.ProcThrottleMinDc -ge 0)         { $throttleMinDc = $json.ProcThrottleMinDc }
        if ($json.EffectiveOverlayGuid)                                                   { $effectiveOverlayGuid = [string]$json.EffectiveOverlayGuid }
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

public static class SurfaceNativeRestore
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_SSH_SEND_COMMAND_TO_TARGET = 0x04001C48;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;
    private const uint CR_SUCCESS = 0x00000000;

    public static readonly Guid GUID_DEVINTERFACE_SURFACE_SERIAL_HUB = new Guid("99bbc337-05d6-4819-b648-39589febb93e");
    private const string FallbackSshPath = @"\\?\ACPI#MSHW0084#2&daba3ff&0#{99bbc337-05d6-4819-b648-39589febb93e}";

    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme", SetLastError = true)]
    private static extern uint PowerSetActiveOverlaySchemeNative(ref Guid OverlaySchemeGuid);

    public static uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid)
    {
        return PowerSetActiveOverlaySchemeNative(ref overlaySchemeGuid);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint pulLen, ref Guid InterfaceClassGuid, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid InterfaceClassGuid, string pDeviceID, [Out] char[] Buffer, uint BufferLen, uint ulFlags);

    private static string ResolveSshDevicePath()
    {
        try
        {
            Guid guid = GUID_DEVINTERFACE_SURFACE_SERIAL_HUB;
            uint len;
            if (CM_Get_Device_Interface_List_SizeW(out len, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) == CR_SUCCESS && len > 1)
            {
                char[] buf = new char[len];
                if (CM_Get_Device_Interface_ListW(ref guid, null, buf, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) == CR_SUCCESS)
                {
                    string multiSz = new string(buf);
                    string[] entries = multiSz.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string entry in entries)
                    {
                        if (entry.IndexOf("MSHW0084", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return entry;
                        }
                    }
                    if (entries.Length > 0)
                    {
                        return entries[0];
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static IntPtr OpenSshDevice()
    {
        string resolved = ResolveSshDevicePath();
        if (!string.IsNullOrEmpty(resolved))
        {
            IntPtr hResolved = CreateFileW(resolved, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hResolved != IntPtr.Zero && hResolved != new IntPtr(-1))
            {
                return hResolved;
            }
        }
        return CreateFileW(FallbackSshPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    public static bool ResetSamFan()
    {
        IntPtr h = OpenSshDevice();
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
        try
        {
            uint ret;
            byte[] rpmZero = { 0x05, 0x01, 0x01, 0x00, 0x0B, 0x02, 0x00, 0x00, 0x00 };
            byte[] profTwo = { 0x05, 0x01, 0x01, 0x00, 0x0E, 0x01, 0x00, 0x02 };
            bool ok1 = DeviceIoControl(h, IOCTL_SSH_SEND_COMMAND_TO_TARGET, rpmZero, (uint)rpmZero.Length, null, 0, out ret, IntPtr.Zero);
            bool ok2 = DeviceIoControl(h, IOCTL_SSH_SEND_COMMAND_TO_TARGET, profTwo, (uint)profTwo.Length, null, 0, out ret, IntPtr.Zero);
            return ok1 && ok2;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
"@

if (-not ("SurfaceNativeRestore" -as [type])) {
    Add-Type -TypeDefinition $nativeCode
}

Write-Host "[*] Applying Surface Overlay Power Setting (AC: $overlayAc, DC: $overlayDc)..." -ForegroundColor Gray
powercfg /setacvalueindex $activeSchemeGuid $subgroup $setting $overlayAc
powercfg /setdcvalueindex $activeSchemeGuid $subgroup $setting $overlayDc

Write-Host "[*] Applying Processor Max Throttle (AC: $throttleAc%, DC: $throttleDc%)..." -ForegroundColor Gray
powercfg /setacvalueindex $activeSchemeGuid $procSub $procMax $throttleAc
powercfg /setdcvalueindex $activeSchemeGuid $procSub $procMax $throttleDc

if ($null -ne $throttleMinAc -and $null -ne $throttleMinDc) {
    Write-Host "[*] Applying Processor Min Throttle (AC: $throttleMinAc%, DC: $throttleMinDc%)..." -ForegroundColor Gray
    powercfg /setacvalueindex $activeSchemeGuid $procSub $procMin $throttleMinAc
    powercfg /setdcvalueindex $activeSchemeGuid $procSub $procMin $throttleMinDc
}

Write-Host "[*] Activating power scheme ($activeSchemeGuid)..." -ForegroundColor Gray
powercfg /setactive $activeSchemeGuid

$parsedGuid = [Guid]::Empty
if (-not [Guid]::TryParse($effectiveOverlayGuid, [ref]$parsedGuid)) {
    $parsedGuid = [Guid]::Empty
}
Write-Host "[*] Restoring Windows 11 Power Overlay Scheme ($parsedGuid)..." -ForegroundColor Gray
$overlayRet = [SurfaceNativeRestore]::PowerSetActiveOverlayScheme($parsedGuid)
if ($overlayRet -eq 0) {
    Write-Host "[OK] Restored Windows 11 Power Overlay Scheme ($parsedGuid)." -ForegroundColor Green
} else {
    Write-Host "[WARN] PowerSetActiveOverlayScheme returned win32 code: $overlayRet" -ForegroundColor Yellow
}

if ([SurfaceNativeRestore]::ResetSamFan()) {
    Write-Host "[OK] Reset Surface SAM Fan to 0 RPM (Auto) and Balanced thermal profile." -ForegroundColor Green
} else {
    Write-Host "[WARN] Could not open Surface SAM controller (MSHW0084) to reset fan override. Run this script as Administrator if a manual/Turbo fan speed was active." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[OK] Successfully restored Surface power and overlay settings to OEM stock defaults!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
