using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SurfaceFanControl.Core.Models;
using SurfaceFanControl.Core.Profiles;

namespace SurfaceFanControl.Core.Hardware
{
    public class SurfacePowerManager : IDisposable
    {
        private Guid _activeSchemeGuid;
        private StockSettingsBackup _stockBackup = new();
        private readonly string _backupFilePath;
        private readonly SurfaceSamClient _samClient;
        private volatile ushort _activeTargetRpm;
        private volatile bool _keepAliveRpmActive;
        private bool _disposed;

        public event Action<CoolingProfile>? ProfileApplied;

        public SurfacePowerManager()
        {
            _samClient = new SurfaceSamClient();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(appData, "SurfaceFanControl");
            Directory.CreateDirectory(appDir);
            _backupFilePath = Path.Combine(appDir, "stock_settings_backup.json");

            Initialize();
        }

        public SurfaceSamClient SamClient => _samClient;

        public StockSettingsBackup StockBackup => _stockBackup;

        public ushort ActiveTargetRpm => _activeTargetRpm;

        public bool IsDirectSamAvailable => _samClient.IsDirectSamAvailable;

        public void Initialize()
        {
            RefreshActiveScheme();

            if (File.Exists(_backupFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_backupFilePath);
                    var loaded = JsonSerializer.Deserialize<StockSettingsBackup>(json);
                    if (loaded != null)
                    {
                        _stockBackup = loaded;
                        return;
                    }
                }
                catch
                {
                    // Fall back to capturing current
                }
            }

            // Capture initial baseline
            CaptureCurrentAsStockBackup();
        }

        public void RefreshActiveScheme()
        {
            uint ret = NativePower.PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid);
            if (ret == NativePower.ERROR_SUCCESS && pGuid != IntPtr.Zero)
            {
                try
                {
                    _activeSchemeGuid = Marshal.PtrToStructure<Guid>(pGuid);
                }
                finally
                {
                    NativePower.LocalFree(pGuid);
                }
            }
            else
            {
                // Fallback to standard Balanced scheme GUID
                _activeSchemeGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
            }
        }

        public void CaptureCurrentAsStockBackup()
        {
            RefreshActiveScheme();
            Guid scheme = _activeSchemeGuid;
            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
            Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
            Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;
            Guid procThrottleMin = NativePower.GUID_PROCTHROTTLEMIN;

            var backup = new StockSettingsBackup
            {
                BackupTimestamp = DateTime.UtcNow,
                ActiveSchemeGuid = scheme.ToString()
            };

            if (NativePower.PowerGetEffectiveOverlayScheme(out Guid effectiveOverlay) == NativePower.ERROR_SUCCESS)
            {
                backup.EffectiveOverlayGuid = effectiveOverlay.ToString();
            }

            if (NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, out uint overlayAc) == NativePower.ERROR_SUCCESS)
            {
                backup.SurfaceOverlayAcIndex = overlayAc;
            }
            if (NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, out uint overlayDc) == NativePower.ERROR_SUCCESS)
            {
                backup.SurfaceOverlayDcIndex = overlayDc;
            }

            if (NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, out uint procMaxAc) == NativePower.ERROR_SUCCESS)
            {
                backup.ProcThrottleMaxAc = procMaxAc;
            }
            if (NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, out uint procMaxDc) == NativePower.ERROR_SUCCESS)
            {
                backup.ProcThrottleMaxDc = procMaxDc;
            }

            if (NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMin, out uint procMinAc) == NativePower.ERROR_SUCCESS)
            {
                backup.ProcThrottleMinAc = procMinAc;
            }
            if (NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMin, out uint procMinDc) == NativePower.ERROR_SUCCESS)
            {
                backup.ProcThrottleMinDc = procMinDc;
            }

            _stockBackup = backup;
            try
            {
                string json = JsonSerializer.Serialize(_stockBackup, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_backupFilePath, json);
            }
            catch { }
        }

        public bool ApplyProfile(CoolingProfile profile)
        {
            RefreshActiveScheme();
            Guid scheme = _activeSchemeGuid;
            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
            Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
            Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;

            // 1. Write legacy Surface Overlay AC / DC values & Processor Max Throttle
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, profile.SurfaceOverlayAcIndex);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, profile.SurfaceOverlayDcIndex);
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, profile.ProcThrottleMaxAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, profile.ProcThrottleMaxDc);
            uint schemeResult = NativePower.PowerSetActiveScheme(IntPtr.Zero, ref scheme);

            // 2. Set Windows 11 Modern Standby Overlay Scheme (updates SMF Core I_SLIDER, O_FAN1_PROFILE, and PL1/PL2)
            Guid overlayGuid = profile.OverlaySchemeGuid;
            uint overlayResult = NativePower.PowerSetActiveOverlayScheme(ref overlayGuid);

            // 3. Direct SAM hardware fan control (when running elevated)
            _activeTargetRpm = profile.TargetFanRpm;
            _keepAliveRpmActive = profile.TargetFanRpm > 0;
            bool samApplied = false;
            if (_samClient.IsDirectSamAvailable)
            {
                _samClient.TrySetFanProfile(profile.SamFanProfile);
                samApplied = _samClient.TrySetFanRpm(profile.TargetFanRpm);
            }

            if (overlayResult == NativePower.ERROR_SUCCESS || schemeResult == NativePower.ERROR_SUCCESS || samApplied)
            {
                ProfileApplied?.Invoke(profile);
                return true;
            }

            return false;
        }

        public bool SetManualFanRpm(ushort targetRpm)
        {
            var manualProfile = CoolingProfile.CreateManual(targetRpm);
            return ApplyProfile(manualProfile);
        }

        public void MaintainFanTargetIfNeeded()
        {
            if (_keepAliveRpmActive && _activeTargetRpm > 0 && _samClient.IsDirectSamAvailable)
            {
                _samClient.TrySetFanRpm(_activeTargetRpm);
            }
        }

        public bool RestoreStockSettings()
        {
            RefreshActiveScheme();
            Guid scheme = _activeSchemeGuid;
            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
            Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
            Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;

            // Restore from backup (fallback to OEM defaults: overlay 1/2, throttle 100/100)
            uint targetOverlayAc = _stockBackup.SurfaceOverlayAcIndex > 0 ? _stockBackup.SurfaceOverlayAcIndex : 1;
            uint targetOverlayDc = _stockBackup.SurfaceOverlayDcIndex > 0 ? _stockBackup.SurfaceOverlayDcIndex : 2;
            uint targetThrottleMaxAc = _stockBackup.ProcThrottleMaxAc > 0 ? _stockBackup.ProcThrottleMaxAc : 100;
            uint targetThrottleMaxDc = _stockBackup.ProcThrottleMaxDc > 0 ? _stockBackup.ProcThrottleMaxDc : 100;

            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, targetOverlayAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, targetOverlayDc);
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, targetThrottleMaxAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, targetThrottleMaxDc);

            uint result = NativePower.PowerSetActiveScheme(IntPtr.Zero, ref scheme);

            Guid stockOverlay = Guid.TryParse(_stockBackup.EffectiveOverlayGuid, out Guid parsedOverlay)
                ? parsedOverlay
                : NativePower.GUID_OVERLAY_BALANCED;
            uint overlayResult = NativePower.PowerSetActiveOverlayScheme(ref stockOverlay);

            _keepAliveRpmActive = false;
            _activeTargetRpm = 0;
            if (_samClient.IsDirectSamAvailable)
            {
                _samClient.TrySetFanRpm(0);
                _samClient.TrySetFanProfile(2);
            }

            if (result == NativePower.ERROR_SUCCESS || overlayResult == NativePower.ERROR_SUCCESS)
            {
                var stockProfile = new CoolingProfile
                {
                    Type = CoolingProfileType.StockReset,
                    Name = "Stock (Restored)",
                    Description = "Original factory baseline settings and automatic SAM fan curve restored.",
                    IconKey = "Reset",
                    SurfaceOverlayAcIndex = targetOverlayAc,
                    SurfaceOverlayDcIndex = targetOverlayDc,
                    ProcThrottleMaxAc = targetThrottleMaxAc,
                    ProcThrottleMaxDc = targetThrottleMaxDc,
                    OverlaySchemeGuid = stockOverlay,
                    SamFanProfile = 2,
                    TargetFanRpm = 0
                };
                ProfileApplied?.Invoke(stockProfile);
                return true;
            }

            return false;
        }

        public bool GetPowerStatus(out bool isPluggedIn, out int batteryPercent)
        {
            isPluggedIn = true;
            batteryPercent = 100;

            if (NativePower.GetSystemPowerStatus(out var status))
            {
                isPluggedIn = status.ACLineStatus == 1;
                batteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_keepAliveRpmActive && _samClient.IsDirectSamAvailable)
            {
                _samClient.TrySetFanRpm(0);
                _samClient.TrySetFanProfile(2);
            }

            _samClient.Dispose();
        }
    }
}
