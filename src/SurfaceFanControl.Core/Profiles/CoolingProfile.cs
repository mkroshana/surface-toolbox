using System;
using SurfaceFanControl.Core.Hardware;

namespace SurfaceFanControl.Core.Profiles
{
    public enum CoolingProfileType
    {
        Quiet,
        Balanced,
        Turbo,
        Manual,
        StockReset
    }

    public class CoolingProfile
    {
        public CoolingProfileType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconKey { get; set; } = string.Empty;
        public uint SurfaceOverlayAcIndex { get; set; }
        public uint SurfaceOverlayDcIndex { get; set; }
        public uint ProcThrottleMaxAc { get; set; }
        public uint ProcThrottleMaxDc { get; set; }
        public Guid OverlaySchemeGuid { get; set; } = NativePower.GUID_OVERLAY_BALANCED;
        public byte SamFanProfile { get; set; } = 2;
        public ushort TargetFanRpm { get; set; } = 0;

        public static CoolingProfile CreateQuiet()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Quiet,
                Name = "Quiet / Silent",
                Description = "Best Power Efficiency overlay (13W–20W PL1 / 28W PL2) with passive cooling priority.",
                IconKey = "Quiet",
                SurfaceOverlayAcIndex = 1,
                SurfaceOverlayDcIndex = 2,
                ProcThrottleMaxAc = 99,
                ProcThrottleMaxDc = 99,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BEST_POWER_EFFICIENCY,
                SamFanProfile = 2,
                TargetFanRpm = 0
            };
        }

        public static CoolingProfile CreateBalanced()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Balanced,
                Name = "Balanced",
                Description = "Surface factory default curve (Recommended overlay, automatic SAM fan control).",
                IconKey = "Balanced",
                SurfaceOverlayAcIndex = 1,
                SurfaceOverlayDcIndex = 2,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BALANCED,
                SamFanProfile = 2,
                TargetFanRpm = 0
            };
        }

        public static CoolingProfile CreateTurbo()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Turbo,
                Name = "Turbo Cooling",
                Description = "Best Performance overlay (26W PL1 / 35W PL2) + active 4200 RPM direct SAM fan cooling.",
                IconKey = "Turbo",
                SurfaceOverlayAcIndex = 5,
                SurfaceOverlayDcIndex = 6,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BEST_PERFORMANCE,
                SamFanProfile = 4,
                TargetFanRpm = 4200
            };
        }

        public static CoolingProfile CreateManual(ushort targetRpm)
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Manual,
                Name = targetRpm > 0 ? $"Manual ({targetRpm} RPM)" : "Manual (Auto / 0 RPM)",
                Description = $"Direct SAM hardware fan override at {targetRpm} RPM.",
                IconKey = "Manual",
                SurfaceOverlayAcIndex = 3,
                SurfaceOverlayDcIndex = 4,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BETTER_PERFORMANCE,
                SamFanProfile = 3,
                TargetFanRpm = targetRpm
            };
        }
    }
}
