using System;
using System.Collections.Generic;
using System.Linq;
using SurfaceFanControl.Core.Hardware;

namespace SurfaceFanControl.Core.Profiles
{
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
            var turbo = CoolingProfile.CreateTurbo();

            _profiles = new Dictionary<CoolingProfileType, CoolingProfile>
            {
                { CoolingProfileType.Quiet, quiet },
                { CoolingProfileType.Balanced, balanced },
                { CoolingProfileType.Turbo, turbo }
            };

            AvailableProfiles = new List<CoolingProfile>
            {
                quiet,
                balanced,
                turbo
            };

            Guid overlay = _powerManager.GetEffectiveOverlayGuid();
            if (overlay == NativePower.GUID_OVERLAY_BEST_POWER_EFFICIENCY)
            {
                _activeProfile = _profiles[CoolingProfileType.Quiet];
            }
            else if (overlay == NativePower.GUID_OVERLAY_BEST_PERFORMANCE)
            {
                _activeProfile = _profiles[CoolingProfileType.Turbo];
            }
            else
            {
                _activeProfile = _profiles[CoolingProfileType.Balanced];
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
            bool ok = _powerManager.ResetFanSpeedOnly();
            if (ok && _activeProfile != null)
            {
                var updated = new CoolingProfile
                {
                    Type = _activeProfile.Type,
                    Name = _activeProfile.Type == CoolingProfileType.Turbo
                        ? "Turbo (Auto Fan)"
                        : (_activeProfile.Type == CoolingProfileType.Manual ? "Manual (Auto Fan)" : _activeProfile.Name),
                    Description = "Automatic SAM fan control active while preserving current power overlay.",
                    IconKey = _activeProfile.IconKey,
                    SurfaceOverlayAcIndex = _activeProfile.SurfaceOverlayAcIndex,
                    SurfaceOverlayDcIndex = _activeProfile.SurfaceOverlayDcIndex,
                    ProcThrottleMaxAc = _activeProfile.ProcThrottleMaxAc,
                    ProcThrottleMaxDc = _activeProfile.ProcThrottleMaxDc,
                    OverlaySchemeGuid = _activeProfile.OverlaySchemeGuid,
                    SamFanProfile = _activeProfile.SamFanProfile,
                    TargetFanRpm = 0
                };
                _activeProfile = updated;
                ActiveProfileChanged?.Invoke(updated);
            }
            return ok;
        }
    }
}
