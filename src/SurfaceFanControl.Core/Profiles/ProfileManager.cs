using System;
using System.Collections.Generic;
using System.Linq;
using SurfaceFanControl.Core.Hardware;

namespace SurfaceFanControl.Core.Profiles
{
    public class ProfileManager
    {
        private readonly SurfacePowerManager _powerManager;
        private CoolingProfile _activeProfile;

        public event Action<CoolingProfile>? ActiveProfileChanged;

        public ProfileManager(SurfacePowerManager powerManager)
        {
            _powerManager = powerManager;

            AvailableProfiles = new List<CoolingProfile>
            {
                CoolingProfile.CreateQuiet(),
                CoolingProfile.CreateBalanced(),
                CoolingProfile.CreateTurbo()
            };

            // Default to Balanced
            _activeProfile = AvailableProfiles[1];

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

        public bool RestoreStock()
        {
            return _powerManager.RestoreStockSettings();
        }

        public bool SetManualRpm(ushort targetRpm)
        {
            return _powerManager.SetManualFanRpm(targetRpm);
        }
    }
}
