using System;
using System.Collections.Generic;
using System.Linq;
using SurfaceFanControl.Core.Hardware;

namespace SurfaceFanControl.Core.Profiles
{
    /// <summary>
    /// Tracks the active power mode and forwards fan requests. The two are independent: choosing
    /// a power mode leaves the fan speed alone, and setting a fan speed leaves the power mode alone.
    /// </summary>
    public class ProfileManager
    {
        private readonly SurfacePowerManager _powerManager;
        private readonly Dictionary<CoolingProfileType, CoolingProfile> _profiles;
        private CoolingProfile _activeProfile;

        public event Action<CoolingProfile>? ActiveProfileChanged;

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

            // Start from the power mode Windows is actually in.
            Guid overlay = _powerManager.GetEffectiveOverlayGuid();
            if (overlay == NativePower.GUID_OVERLAY_BEST_POWER_EFFICIENCY)
            {
                _activeProfile = quiet;
            }
            else if (overlay == NativePower.GUID_OVERLAY_BETTER_PERFORMANCE)
            {
                _activeProfile = betterPerformance;
            }
            else if (overlay == NativePower.GUID_OVERLAY_BEST_PERFORMANCE)
            {
                _activeProfile = bestPerformance;
            }
            else
            {
                _activeProfile = balanced;
            }

            _powerManager.ProfileApplied += (p) =>
            {
                _activeProfile = p;
                ActiveProfileChanged?.Invoke(p);
            };
        }

        public IReadOnlyList<CoolingProfile> AvailableProfiles { get; }

        public CoolingProfile ActiveProfile => _activeProfile;

        public bool SetProfile(CoolingProfileType type)
        {
            if (type == CoolingProfileType.StockReset)
            {
                return RestoreStock();
            }

            var target = AvailableProfiles.FirstOrDefault(p => p.Type == type);
            if (target != null)
            {
                return _powerManager.ApplyProfile(target);
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
