using System;
using System.Collections.Generic;
using System.Linq;
using SurfaceToolbox.Core.Hardware;

namespace SurfaceToolbox.Core.Profiles
{
    /// <summary>
    /// Tracks the power mode of each power source and forwards fan requests. Windows keeps one
    /// power mode for "plugged in" and another for "on battery" and switches between them itself.
    /// Power mode and fan speed are independent: choosing a power mode leaves the fan speed
    /// alone, and setting a fan speed leaves the power mode alone.
    /// </summary>
    public class ProfileManager
    {
        private readonly SurfacePowerManager _powerManager;
        private readonly Dictionary<CoolingProfileType, CoolingProfile> _profiles;
        private readonly Dictionary<PowerSource, CoolingProfile> _activeProfiles = new();

        /// <summary>Raised when a source's power mode changes, with the source it changed for.</summary>
        public event Action<CoolingProfile, PowerSource>? ActiveProfileChanged;

        public ProfileManager(SurfacePowerManager powerManager)
        {
            _powerManager = powerManager;

            var quiet = CoolingProfile.CreateQuiet();
            var balanced = CoolingProfile.CreateBalanced();
            var betterPerformance = CoolingProfile.CreateBetterPerformance();
            var bestPerformance = CoolingProfile.CreateBestPerformance();

            _profiles = new Dictionary<CoolingProfileType, CoolingProfile>
            {
                { CoolingProfileType.Quiet, quiet },
                { CoolingProfileType.Balanced, balanced },
                { CoolingProfileType.BetterPerformance, betterPerformance },
                { CoolingProfileType.BestPerformance, bestPerformance }
            };

            AvailableProfiles = new List<CoolingProfile>
            {
                quiet,
                balanced,
                betterPerformance,
                bestPerformance
            };

            // Start from the power modes Windows actually has for each source.
            _activeProfiles[PowerSource.PluggedIn] = ProfileForOverlay(_powerManager.GetConfiguredOverlay(PowerSource.PluggedIn));
            _activeProfiles[PowerSource.Battery] = ProfileForOverlay(_powerManager.GetConfiguredOverlay(PowerSource.Battery));

            _powerManager.ProfileApplied += (profile, source) =>
            {
                _activeProfiles[source] = profile;
                ActiveProfileChanged?.Invoke(profile, source);
            };
        }

        public IReadOnlyList<CoolingProfile> AvailableProfiles { get; }

        /// <summary>The power mode of the power source in use right now.</summary>
        public CoolingProfile ActiveProfile => _activeProfiles[_powerManager.CurrentPowerSource];

        public CoolingProfile GetActiveProfile(PowerSource source) => _activeProfiles[source];

        // An unreadable mode (older Windows, source not in use) is shown as Balanced.
        private CoolingProfile ProfileForOverlay(Guid? overlay)
        {
            if (overlay == NativePower.GUID_OVERLAY_BEST_POWER_EFFICIENCY)
                return _profiles[CoolingProfileType.Quiet];
            if (overlay == NativePower.GUID_OVERLAY_BETTER_PERFORMANCE)
                return _profiles[CoolingProfileType.BetterPerformance];
            if (overlay == NativePower.GUID_OVERLAY_BEST_PERFORMANCE)
                return _profiles[CoolingProfileType.BestPerformance];
            return _profiles[CoolingProfileType.Balanced];
        }

        /// <summary>Sets the power mode of the power source in use.</summary>
        public bool SetProfile(CoolingProfileType type)
        {
            return SetProfile(type, _powerManager.CurrentPowerSource);
        }

        /// <summary>Sets the power mode of one power source, leaving the other as it is.</summary>
        public bool SetProfile(CoolingProfileType type, PowerSource source)
        {
            if (type == CoolingProfileType.StockReset)
            {
                return RestoreStock();
            }

            var target = AvailableProfiles.FirstOrDefault(p => p.Type == type);
            if (target != null)
            {
                return _powerManager.ApplyProfile(target, source);
            }

            return false;
        }

        public bool SwitchProfile(CoolingProfileType type) => SetProfile(type);

        public bool RestoreStock()
        {
            return _powerManager.RestoreStockSettings();
        }

        public bool SetManualRpm(ushort targetRpm)
        {
            return _powerManager.SetManualFanRpm(targetRpm);
        }

        public bool SetManualRpm(int targetRpm)
        {
            return _powerManager.SetManualFanRpm((ushort)Math.Clamp(targetRpm, 0, 5000));
        }

        public bool SetManualFanRpm(ushort targetRpm)
        {
            return _powerManager.SetManualFanRpm(targetRpm);
        }

        public bool SetManualFanRpm(int targetRpm)
        {
            return _powerManager.SetManualFanRpm((ushort)Math.Clamp(targetRpm, 0, 5000));
        }

        public bool ResetFanSpeedOnly()
        {
            return _powerManager.ResetFanSpeedOnly();
        }
    }
}
