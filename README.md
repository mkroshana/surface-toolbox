# Surface Fan Control for Microsoft Surface Devices

A native Windows 11 hardware fan controller and thermal telemetry dashboard for Microsoft Surface devices (engineered and verified on **Surface Pro 9**). It combines direct **Surface System Aggregator Module (SAM)** serial hub commands (`TC_FAN = 0x05`), **Surface System Management Framework (SMF Core)** real-time channel queries, and **Windows 11 Modern Standby Power Overlays**.

---

## Hardware Architecture & Reverse-Engineered Protocols

Unlike standard PCs with SuperI/O PWM registers, modern Intel Surface devices (such as the Surface Pro 9) manage cooling and power limits across three layers in the OEM driver stack:

1. **`SurfaceSerialHubDriver.sys` (`ACPI\MSHW0084`)** — The low-level serial transport to the physical SAM microcontroller (`TC_FAN = 0x05`). Requires Administrator privileges to open from user mode.
2. **`SurfaceSystemManagementFramework.sys` (`SMF Core`)** — The kernel thermal/power state machine that computes fan curves (`O_FAN1_PROFILE`, `O_FAN1_ACTIVE_RPM`) and CPU package power limits (`PL1` / `PL2`). Accessible from user mode without Administrator privileges.
3. **`SurfaceSmfClient.sys` (`SurfaceSmfFanClient` & `SurfaceSmfSystemClient`)** — Listens to Windows `PO_NOTIFY_EFFECTIVE_POWER_MODE` (`powrprof.dll!PowerSetActiveOverlayScheme`), feeds the slider state (`I_SLIDER`) into `SMF Core`, and forwards `SMF Core` fan output channels to `MSHW0084`.

> **Why Legacy Power Plan Writes Fail on Surface Pro 9:** Writing classic power plan indices (`PowerWriteACValueIndex` / `PowerSetActiveScheme`) does not trigger `PO_NOTIFY_EFFECTIVE_POWER_MODE` on Modern Standby (`S0ix`) systems. `SurfaceSmfClient.sys` (`RVA 0x4C210`) only updates the SMF slider when the Windows Effective Power Overlay (`PowerSetActiveOverlayScheme`) changes.

---

### 1. Direct SAM Fan Protocol (`SurfaceSerialHubDriver` / `MSHW0084`)

- **Device Interface GUID:** `{99bbc337-05d6-4819-b648-39589febb93e}`
- **Device Path (Surface Pro 9):** `\\?\ACPI#MSHW0084#2&daba3ff&0#{99bbc337-05d6-4819-b648-39589febb93e}`
- **Access Requirements:** `GENERIC_READ | GENERIC_WRITE`, `FILE_SHARE_READ | FILE_SHARE_WRITE`, Administrator (`requireAdministrator`)
- **IOCTL Code:** `IOCTL_SSH_SEND_COMMAND_TO_TARGET = 0x04001C48`

#### 7-Byte SSH Command Header (`lpInBuffer`)

| Byte Offset | Type | Field | Fan Value | Description |
| :--- | :--- | :--- | :--- | :--- |
| `0` | `uint8` | `TargetCategory` (`TC`) | `0x05` | `TC_FAN` |
| `1` | `uint8` | `TargetID` (`TID`) | `0x01` | SAM primary MCU |
| `2` | `uint8` | `InstanceID` (`IID`) | `0x01` | Fan index (`0x01` = Fan 1, `0x02` = Fan 2, `0x03` = Fan 3) |
| `3` | `uint8` | `HasResponse` | `0x00` or `0x01` | `0x01` if expecting response bytes in `lpOutBuffer`; `0x00` for write-only commands |
| `4` | `uint8` | `CommandID` (`CID`) | See below | Fan command selector |
| `5..6` | `uint16` | `PayloadLength` | `0..4` | Little-endian byte length of payload immediately following byte `6` |
| `7..N` | `byte[]` | `Payload` | Variable | Command payload bytes |

#### Verified `TC_FAN` (`0x05`) Commands

| `CID` | Operation | `HasResponse` | Input Bytes (`Hex`) | Output Buffer | Driver Reference |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `0x01` | **Read Live Tachometer RPM** | `0x01` | `05 01 01 01 01 00 00` (7B) | `2 bytes` (`uint16` live rotor RPM) | `SurfaceSystemTelemetryDriver.sys` (`RVA 0x8FD4`) |
| `0x0B` | **Set Target Fan RPM** | `0x00` | `05 01 01 00 0B 02 00 <lo> <hi>` (9B, `uint16` RPM; `0` = Auto) | `null` (`0B`) | `SurfaceSmfClient.sys` (`RVA 0x389D0`) |
| `0x0C` | **Set Fan Max SPL (Sones)** | `0x00` | `05 01 01 00 0C 04 00 <float32>` (11B) | `null` (`0B`) | `SurfaceSmfClient.sys` (`RVA 0x38720`) |
| `0x0E` | **Set Fan Profile** | `0x00` | `05 01 01 00 0E 01 00 <uint8>` (8B; `2`=Balanced, `3`=Better, `4`=Best) | `null` (`0B`) | `SurfaceSmfClient.sys` (`RVA 0x38C74`) |
| `0x0F` | **Set CS Fan Request** | `0x00` | `05 01 01 00 0F 01 00 <uint8>` (8B) | `null` (`0B`) | `SurfaceSmfClient.sys` (`RVA 0x384A8`) |

> **Hardware Behavior Note:** From a dead stop (`0 RPM`), the Surface Pro 9 SAM firmware applies a ~4.5-second startup delay and a ~350–500 RPM/sec slew-rate filter. Because `SMF Core` can periodically re-send its computed RPM, a manual RPM override refreshes `CID = 0x0B` on each 1-second poll cycle and restores `0 RPM` (`Auto`) on fan reset or application exit.
>
> **Measured on a warm, charging Surface Pro 9 (2026-11-13):** while the firmware's own thermal control is driving the fan, `CID = 0x0B` is accepted but has no effect, above or below the current speed and even when re-sent ten times a second. `O_FAN1_ACTIVE_RPM` reads `0` in that state, so the SAM is running its own curve. The app compares the tachometer with the requested RPM and reports when a target is not being applied. Setting the SAM fan profile directly (`CID = 0x0E`) with the power mode left alone moved the fan by only about 100 RPM.

---

### 2. SMF Core Channel Query Protocol (`SurfaceSystemManagementFramework`)

- **Device Interface GUID:** `{b98aea2e-7ee8-403c-8232-f0d48d5dd338}`
  - *Note:* Three Surface drivers register this interface GUID (`SurfaceSystemTelemetryDriver`, `SurfacePowerStateDriver`, and `SurfaceSystemManagementFramework`). Always filter the `CM_Get_Device_Interface_ListW` multi-SZ list for `"SurfaceSystemManagementFramework"`.
- **Device Path (Surface Pro 9):** `\\?\{16C280BF-D438-43C1-BBF3-688CA0D43E2F}#SurfaceSystemManagementFramework#3&1e75d51e&0&SID_DEVICE_01#{b98aea2e-7ee8-403c-8232-f0d48d5dd338}`
- **Access Requirements:** Opens with `GENERIC_READ | GENERIC_WRITE` **without Administrator privileges**.
- **IOCTL Code:** `IOCTL_SMF_CHANNEL_ACTION = 0x1FAA000C` (`SmfCore_SptActions` at `RVA 0x54D08`)
  - **Input Buffer (`40 bytes`):** `Guid InterfaceGuid (16B)` + `uint32 ChannelId (4B)` + `uint32 ActionCmd (4B, 0 = Read)` + `int32 SubCmd (4B, 0 = DataGet, 1 = OnlineStatusGet, 2 = DomainExistGet, 3 = ConfiguredGet)` + `12 bytes zero padding`.
  - **Output Buffer (`16 bytes`):** `int64 Value` at offset `0`.
  - *Note:* Override write actions (`ActionCmd = 1` `ForceValueWrite`, `ActionCmd = 4` `ForcedModeSet`) return `ERROR_REQUEST_ABORTED (1235)` / `STATUS_REQUEST_ABORTED (0xC0000240)` on retail drivers because `[r14+1] == 0`.

#### Verified SMF Core Channels

| Interface Name | Interface GUID | Channel ID | Meaning / Units |
| :--- | :--- | :--- | :--- |
| **Fan Interface** | `b620798f-6fb0-42ef-b9b3-d2370402e089` | `0x65` (`101`) | `O_FAN1_ACTIVE_RPM` (SMF computed target RPM) |
| **Fan Interface** | `b620798f-6fb0-42ef-b9b3-d2370402e089` | `0x6F` (`111`) | `O_FAN1_MAX_SPL` (Acoustic limit) |
| **Fan Interface** | `b620798f-6fb0-42ef-b9b3-d2370402e089` | `0xC9` (`201`) | `O_FAN1_PROFILE` (`2` = Balanced/Quiet, `3` = Better Perf, `4` = Best Perf) |
| **Fan Interface** | `b620798f-6fb0-42ef-b9b3-d2370402e089` | `0x12D` (`301`) | `O_FAN_CS_REQUEST` (`1` = Normal) |
| **Power Limits** | `fb805e77-616e-4ea8-bac1-6859a6c2d4b1` | `0x01`, `0x02` | CPU `PL2` (`0x01`) and `PL1` (`0x02`) in milliwatts (`mW`) |
| **Processor Power** | `049e36f2-906d-47f2-aca4-610552bfb1fa` | `0x01` | Live CPU Package Power (`0x01`) in milliwatts (`mW`, on-demand helper via `TryReadPackagePower`) |
| **System Slider** | `87008365-ea3a-461c-b83d-6ac6e8db2f8a` | `0x01` | `I_SLIDER` state |

---

### 3. Windows 11 Modern Standby Power Overlays (`powrprof.dll`)

Calling `PowerSetActiveOverlayScheme(ref Guid overlayGuid)` immediately transitions `SMF Core` and updates both the SAM fan profile and CPU power limits:

| Mode | Overlay Scheme GUID | SMF `O_FAN1_PROFILE` | SMF `PL1 / PL2` |
| :--- | :--- | :---: | :---: |
| **Quiet** | `961cc777-2547-4f9d-8174-7d86181b8a7a` (*Best Power Efficiency* + `99% max processor state`) | `2` | `13 W / 28 W` |
| **Balanced** | `00000000-0000-0000-0000-000000000000` (*Recommended*) | `2` | `13 W–21 W / 28 W` |
| **Better Performance** | `3af9b8d9-7c97-431d-ad78-34a8bfea439f` (*Better Performance*) | `3` | `23 W–28 W / 28 W–30 W` (measured) |
| **Best Performance** | `ded574b5-45a0-4f42-8737-46345c09c238` (*Best Performance*) | `4` | `35 W / 35 W` (measured) |

**Power mode and fan speed are separate settings in the app.** Choosing a power mode sets the overlay (and Quiet's 99% processor cap) and leaves any fan speed target alone; setting a fan speed sends only the SAM RPM command and leaves the power mode alone. The fan's automatic behaviour still follows the power mode, because `SMF Core` selects the fan profile from the overlay. Windows keeps one power mode for "plugged in" and one for "on battery" and switches between them itself. `PowerSetActiveOverlayScheme` changes only the mode of the source in use; the app sets the other source through `PowerSetUserConfiguredACPowerMode` / `PowerSetUserConfiguredDCPowerMode` (exports of `powrprof.dll`, not in the public SDK headers, verified on Windows 11 build 26300). The dashboard has a selector for which source the Power Mode cards edit, Quiet's 99% processor cap is written for that source only, and on exit both sources are put back to what they were before the app changed them.

---

## Project Structure

- `src/SurfaceFanControl.Core/Hardware/SurfaceSamClient.cs` — Direct `MSHW0084` (`0x04001C48`) fan RPM/profile control and `SMF Core` (`0x1FAA000C`) telemetry client.
- `src/SurfaceFanControl.Core/Hardware/NativePower.cs` — `powrprof.dll` P/Invoke declarations (`PowerGetEffectiveOverlayScheme`, `PowerSetActiveOverlayScheme`, legacy scheme APIs).
- `src/SurfaceFanControl.Core/Hardware/SurfacePowerManager.cs` — Coordinates overlay switching, direct SAM fan commands, periodic RPM keep-alive, baseline backup (`%LOCALAPPDATA%\SurfaceFanControl\stock_settings_backup.json`), and, on disposal, restoration of the power settings as they were before the app changed them (`session_baseline.json` in the same folder, kept only while changes are outstanding) and `0 RPM` automatic fan control.
- `src/SurfaceFanControl.Core/Hardware/SurfaceThermalMonitor.cs` — 1-second hardware polling loop reading ACPI thermal zones (`\_SB.TZ01`..`\_SB.TZ0B`), live SAM tachometer RPM, SMF fan profile, and `PL1`/`PL2` wattages.
- `src/SurfaceFanControl.UI/` — WPF dashboard (`MainWindow.xaml`, `MainViewModel.cs`) and system tray integration (`TrayIconManager.cs`). Configured with `requireAdministrator` in `app.manifest`.

---

## Build, Run & Verification

### Compiled Executable Location
```text
c:\GitHub\Surface-FanControl\src\SurfaceFanControl.UI\bin\Release\net8.0-windows\SurfaceFanControl.UI.exe
```

### Commands
```powershell
# Build Release binaries
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# Build and launch the WPF application (prompts UAC for direct SAM access)
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Run

# Run the automated hardware verification suite (requires PowerShell 7 / pwsh for .NET 8 assembly loading)
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_verification.ps1

# Restore OEM stock power overlay and reset SAM fan to 0 RPM (Auto)
# Note: Administrator privileges are required to reset a direct SAM fan speed override (MSHW0084)
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\restore_stock_settings.ps1
# (or: .\build.ps1 -RestoreStock)
```
