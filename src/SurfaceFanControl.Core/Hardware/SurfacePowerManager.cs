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
        /// <summary>How far the measured fan speed may sit from the requested RPM and still count as applied.</summary>
        public const int FanTargetToleranceRpm = 350;

        // Worst case from a stopped fan: ~4.5 s firmware start delay plus a ~350 RPM/s ramp to 4800 RPM.
        private const long FanTargetSettleMs = 20000;

        private Guid _activeSchemeGuid;
        private StockSettingsBackup _stockBackup = new();
        private SessionBaseline? _sessionBaseline;
        private long _targetSetTickMs;
        private readonly string _backupFilePath;
        private readonly string _sessionBaselinePath;
        private readonly SurfaceSamClient _samClient;
        private readonly object _fanLock = new();
        private volatile ushort _activeTargetRpm;
        private volatile byte _activeSamFanProfile = 2;
        private volatile bool _keepAliveRpmActive;
        private bool _samModified;
        private bool _powerModified;
        private bool _disposed;

        public event Action<CoolingProfile>? ProfileApplied;

        public SurfacePowerManager()
        {
            _samClient = new SurfaceSamClient();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(appData, "SurfaceFanControl");
            Directory.CreateDirectory(appDir);
            _backupFilePath = Path.Combine(appDir, "stock_settings_backup.json");
            _sessionBaselinePath = Path.Combine(appDir, "session_baseline.json");

            Initialize();
            LoadPendingSessionBaseline();
        }

        public SurfaceSamClient SamClient => _samClient;

        public StockSettingsBackup StockBackup => _stockBackup;

        public ushort ActiveTargetRpm => _activeTargetRpm;

        public bool IsDirectSamAvailable => _samClient.IsDirectSamAvailable;

        /// <summary>
        /// True when the most recent profile apply or stock restore could not switch the
        /// Windows power mode overlay.
        /// </summary>
        public bool LastOverlayFailed { get; private set; }

        /// <summary>
        /// True when the most recent profile apply or stock restore reached the SAM but its
        /// fan RPM command failed.
        /// </summary>
        public bool LastFanCommandFailed { get; private set; }

        /// <summary>
        /// Compares the measured fan speed with the RPM last requested from the SAM. The SAM accepts
        /// an RPM command even when its own thermal control then ignores it, so the command's result
        /// alone does not show whether the fan is following the request.
        /// </summary>
        public FanTargetState EvaluateFanTarget(int measuredRpm)
        {
            int target = _activeTargetRpm;
            if (target <= 0)
                return FanTargetState.Auto;

            if (Math.Abs(measuredRpm - target) <= FanTargetToleranceRpm)
                return FanTargetState.Applied;

            if (Environment.TickCount64 - Interlocked.Read(ref _targetSetTickMs) < FanTargetSettleMs)
                return FanTargetState.Settling;

            return measuredRpm > target ? FanTargetState.HeldAboveTarget : FanTargetState.NotApplied;
        }

        public Guid GetEffectiveOverlayGuid()
        {
            if (NativePower.PowerGetEffectiveOverlayScheme(out Guid effectiveOverlay) == NativePower.ERROR_SUCCESS)
            {
                return effectiveOverlay;
            }
            return NativePower.GUID_OVERLAY_BALANCED;
        }

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
            CaptureSessionBaseline(scheme);
            _powerModified = true;
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, profile.SurfaceOverlayAcIndex);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, profile.SurfaceOverlayDcIndex);
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, profile.ProcThrottleMaxAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, profile.ProcThrottleMaxDc);
            uint schemeResult = NativePower.PowerSetActiveScheme(IntPtr.Zero, ref scheme);

            // 2. Set Windows 11 Modern Standby Overlay Scheme (updates SMF Core I_SLIDER, O_FAN1_PROFILE, and PL1/PL2)
            Guid overlayGuid = profile.OverlaySchemeGuid;
            uint overlayResult = NativePower.PowerSetActiveOverlayScheme(ref overlayGuid);
            LastOverlayFailed = overlayResult != NativePower.ERROR_SUCCESS;
            LastFanCommandFailed = false;

            // 3. Keep the SAM's fan profile in step with the power mode, as SMF itself does.
            //    The fan speed target is a separate setting and is deliberately left alone here.
            bool profileSet = false;
            lock (_fanLock)
            {
                if (_disposed)
                    return false;

                _activeSamFanProfile = profile.SamFanProfile;
                if (_samClient.IsDirectSamAvailable)
                {
                    profileSet = _samClient.TrySetFanProfile(profile.SamFanProfile);
                    if (profileSet)
                    {
                        _samModified = true;
                    }
                }
            }

            if (overlayResult == NativePower.ERROR_SUCCESS || schemeResult == NativePower.ERROR_SUCCESS || profileSet)
            {
                ProfileApplied?.Invoke(profile);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Asks the SAM for a fixed fan speed; 0 returns the fan to automatic control. This is a
        /// fan-only setting: the power mode and power-plan values are not touched.
        /// </summary>
        public bool SetManualFanRpm(ushort targetRpm)
        {
            ushort effectiveRpm = targetRpm == 0
                ? (ushort)0
                : Math.Clamp(targetRpm, SurfaceSamClient.MinManualFanRpm, SurfaceSamClient.MaxManualFanRpm);

            lock (_fanLock)
            {
                if (_disposed)
                    return false;

                LastFanCommandFailed = false;
                if (!_samClient.IsDirectSamAvailable)
                    return false;

                bool rpmSet = _samClient.TrySetFanRpm(effectiveRpm);
                LastFanCommandFailed = !rpmSet;
                if (!rpmSet)
                    return false; // Any earlier target and its keep-alive stay as they were.

                _samModified = true;
                if (effectiveRpm > 0)
                {
                    Interlocked.Exchange(ref _targetSetTickMs, Environment.TickCount64);
                    _activeTargetRpm = effectiveRpm;
                    _keepAliveRpmActive = true;
                }
                else
                {
                    _activeTargetRpm = 0;
                    _keepAliveRpmActive = false;
                }

                return true;
            }
        }

        // The fan profile SMF currently wants for the active power mode (2 = Balanced, 3 = Better,
        // 4 = Best). Read live so a power mode changed outside the app is respected; falls back
        // to the profile of the last power mode the app applied.
        private byte CurrentFanProfile()
        {
            if (_samClient.TryReadSmfFanProfile(out int smfProfile) && smfProfile >= 2 && smfProfile <= 4)
                return (byte)smfProfile;

            return _activeSamFanProfile;
        }

        public bool ResetFanSpeedOnly()
        {
            lock (_fanLock)
            {
                if (_disposed)
                    return false;

                _keepAliveRpmActive = false;
                _activeTargetRpm = 0;

                if (!_samClient.IsDirectSamAvailable)
                    return false;

                bool rpmOk = _samClient.TrySetFanRpm(0);
                _samClient.TrySetFanProfile(CurrentFanProfile());
                if (rpmOk)
                {
                    _samModified = true;
                }
                return rpmOk;
            }
        }

        public void MaintainFanTargetIfNeeded()
        {
            lock (_fanLock)
            {
                if (_disposed)
                    return;

                if (_keepAliveRpmActive && _activeTargetRpm > 0 && _samClient.IsDirectSamAvailable)
                {
                    _samClient.TrySetFanProfile(CurrentFanProfile());
                    _samClient.TrySetFanRpm(_activeTargetRpm);
                }
            }
        }

        public bool RestoreStockSettings()
        {
            RefreshActiveScheme();
            Guid scheme = Guid.TryParse(_stockBackup.ActiveSchemeGuid, out Guid parsedScheme) && parsedScheme != Guid.Empty
                ? parsedScheme
                : _activeSchemeGuid;
            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
            Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
            Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;
            Guid procThrottleMin = NativePower.GUID_PROCTHROTTLEMIN;

            // Restore from backup (fallback to OEM defaults: overlay 1/2, throttle 100/100, min throttle 5/1)
            uint targetOverlayAc = _stockBackup.SurfaceOverlayAcIndex > 0 ? _stockBackup.SurfaceOverlayAcIndex : 1;
            uint targetOverlayDc = _stockBackup.SurfaceOverlayDcIndex > 0 ? _stockBackup.SurfaceOverlayDcIndex : 2;
            uint targetThrottleMaxAc = _stockBackup.ProcThrottleMaxAc > 0 ? _stockBackup.ProcThrottleMaxAc : 100;
            uint targetThrottleMaxDc = _stockBackup.ProcThrottleMaxDc > 0 ? _stockBackup.ProcThrottleMaxDc : 100;
            uint targetThrottleMinAc = _stockBackup.ProcThrottleMinAc <= 100 ? _stockBackup.ProcThrottleMinAc : 5;
            uint targetThrottleMinDc = _stockBackup.ProcThrottleMinDc <= 100 ? _stockBackup.ProcThrottleMinDc : 1;

            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, targetOverlayAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, targetOverlayDc);
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, targetThrottleMaxAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, targetThrottleMaxDc);
            NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMin, targetThrottleMinAc);
            NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMin, targetThrottleMinDc);

            uint result = NativePower.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (result == NativePower.ERROR_SUCCESS)
            {
                _activeSchemeGuid = scheme;
            }

            Guid stockOverlay = Guid.TryParse(_stockBackup.EffectiveOverlayGuid, out Guid parsedOverlay)
                ? parsedOverlay
                : NativePower.GUID_OVERLAY_BALANCED;
            uint overlayResult = NativePower.PowerSetActiveOverlayScheme(ref stockOverlay);

            // An explicit reset to stock replaces whatever was there before the session, so
            // there is nothing left for the exit path to put back.
            ClearSessionBaseline();
            LastOverlayFailed = overlayResult != NativePower.ERROR_SUCCESS;
            LastFanCommandFailed = false;

            lock (_fanLock)
            {
                _keepAliveRpmActive = false;
                _activeTargetRpm = 0;
                if (_samClient.IsDirectSamAvailable)
                {
                    LastFanCommandFailed = !_samClient.TrySetFanRpm(0);
                    _samClient.TrySetFanProfile(2);
                }
                _samModified = false;
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
                    SamFanProfile = 2
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
            lock (_fanLock)
            {
                if (_disposed) return;
                _disposed = true;

                bool shouldResetSam = _keepAliveRpmActive || _samModified;
                _keepAliveRpmActive = false;
                _activeTargetRpm = 0;
                _samModified = false;

                if (shouldResetSam && _samClient.IsDirectSamAvailable)
                {
                    _samClient.TrySetFanRpm(0);
                    _samClient.TrySetFanProfile(CurrentFanProfile());
                }

                // A profile also changed the power plan and power mode (e.g. Quiet's 99% processor
                // cap). Put those back to what they were before the app touched them so they do
                // not outlive it. Done after the fan reset so SMF re-sends its own fan profile.
                if (_powerModified)
                {
                    RestoreSessionBaseline();
                }

                _samClient.Dispose();
            }
        }

        private static bool IsOnAcPower()
        {
            return !NativePower.GetSystemPowerStatus(out var status) || status.ACLineStatus == 1;
        }

        // A session baseline still on disk means the previous run changed power settings and
        // ended without putting them back (crash or forced kill). Adopt it, so this run's exit
        // restores the real earlier state rather than the leftovers of that run.
        private void LoadPendingSessionBaseline()
        {
            try
            {
                if (!File.Exists(_sessionBaselinePath))
                    return;

                var loaded = JsonSerializer.Deserialize<SessionBaseline>(File.ReadAllText(_sessionBaselinePath));
                if (loaded != null)
                {
                    _sessionBaseline = loaded;
                    _powerModified = true;

                    // That run may also have left a fan RPM override behind, so make this
                    // run's exit return the fan to automatic control as well.
                    _samModified = true;
                }
            }
            catch
            {
                // Unreadable file: a fresh baseline is captured on the first change instead.
            }
        }

        // Records the settings a profile is about to overwrite, the first time each is touched.
        private void CaptureSessionBaseline(Guid scheme)
        {
            bool changed = false;

            if (_sessionBaseline == null)
            {
                Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
                Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
                Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
                Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;

                var baseline = new SessionBaseline { SchemeGuid = scheme.ToString() };

                if (NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, out uint overlayAc) == NativePower.ERROR_SUCCESS)
                    baseline.SurfaceOverlayAcIndex = overlayAc;
                if (NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, out uint overlayDc) == NativePower.ERROR_SUCCESS)
                    baseline.SurfaceOverlayDcIndex = overlayDc;
                if (NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, out uint throttleAc) == NativePower.ERROR_SUCCESS)
                    baseline.ProcThrottleMaxAc = throttleAc;
                if (NativePower.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, out uint throttleDc) == NativePower.ERROR_SUCCESS)
                    baseline.ProcThrottleMaxDc = throttleDc;

                _sessionBaseline = baseline;
                changed = true;
            }

            // PowerSetActiveOverlayScheme only changes the power mode of the current power source,
            // so the mode is recorded per source, the first time the app is about to change it.
            if (IsOnAcPower())
            {
                if (_sessionBaseline.AcOverlayGuid == null)
                {
                    _sessionBaseline.AcOverlayGuid = GetEffectiveOverlayGuid().ToString();
                    changed = true;
                }
            }
            else if (_sessionBaseline.DcOverlayGuid == null)
            {
                _sessionBaseline.DcOverlayGuid = GetEffectiveOverlayGuid().ToString();
                changed = true;
            }

            if (changed)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_sessionBaseline, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_sessionBaselinePath, json);
                }
                catch { }
            }
        }

        // Puts back the power-plan values and the power mode recorded before the app changed them.
        // Only the current power source's mode can be set; the other source keeps what it has.
        private void RestoreSessionBaseline()
        {
            SessionBaseline? baseline = _sessionBaseline;
            if (baseline == null)
            {
                _powerModified = false;
                return;
            }

            RefreshActiveScheme();
            Guid scheme = Guid.TryParse(baseline.SchemeGuid, out Guid parsedScheme) && parsedScheme != Guid.Empty
                ? parsedScheme
                : _activeSchemeGuid;
            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;
            Guid procSubgroup = NativePower.GUID_PROCESSOR_SUBGROUP;
            Guid procThrottleMax = NativePower.GUID_PROCTHROTTLEMAX;

            if (baseline.SurfaceOverlayAcIndex is uint overlayAc)
                NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, overlayAc);
            if (baseline.SurfaceOverlayDcIndex is uint overlayDc)
                NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, overlayDc);
            if (baseline.ProcThrottleMaxAc is uint throttleAc)
                NativePower.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, throttleAc);
            if (baseline.ProcThrottleMaxDc is uint throttleDc)
                NativePower.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref procSubgroup, ref procThrottleMax, throttleDc);

            // Re-activate the scheme that is active now so the values take effect, without
            // switching the user back to a different plan.
            Guid active = _activeSchemeGuid;
            NativePower.PowerSetActiveScheme(IntPtr.Zero, ref active);

            string? savedOverlay = IsOnAcPower() ? baseline.AcOverlayGuid : baseline.DcOverlayGuid;
            if (Guid.TryParse(savedOverlay, out Guid overlay))
            {
                NativePower.PowerSetActiveOverlayScheme(ref overlay);
            }

            ClearSessionBaseline();
        }

        private void ClearSessionBaseline()
        {
            _sessionBaseline = null;
            _powerModified = false;
            try
            {
                File.Delete(_sessionBaselinePath);
            }
            catch { }
        }
    }
}
