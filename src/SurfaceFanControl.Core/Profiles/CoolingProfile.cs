using System;
using SurfaceFanControl.Core.Hardware;

namespace SurfaceFanControl.Core.Profiles
{
    public enum CoolingProfileType
    {
        Quiet,
        Balanced,
        BetterPerformance,
        BestPerformance,
        StockReset
    }

    /// <summary>
    /// A power mode: the Windows power-mode overlay plus the power-plan values that go with it.
    /// Fan speed is a separate setting (see <c>SurfacePowerManager.SetManualFanRpm</c>) and is
    /// not part of a profile, so applying one never changes a fan speed the user has set.
    /// </summary>
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

        /// <summary>The fan profile SMF selects for this power mode (2 = Balanced, 3 = Better, 4 = Best).</summary>
        public byte SamFanProfile { get; set; } = 2;

        public static CoolingProfile CreateQuiet()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Quiet,
                Name = "Quiet",
                Description = "Best Power Efficiency mode with the processor capped at 99% (turbo boost off).",
                IconKey = "Quiet",
                SurfaceOverlayAcIndex = NativePower.SURFACE_OVERLAY_BALANCED_AC,
                SurfaceOverlayDcIndex = NativePower.SURFACE_OVERLAY_BALANCED_DC,
                ProcThrottleMaxAc = 99,
                ProcThrottleMaxDc = 99,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BEST_POWER_EFFICIENCY,
                SamFanProfile = 2
            };
        }

        public static CoolingProfile CreateBalanced()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.Balanced,
                Name = "Balanced",
                Description = "Windows Recommended mode, the Surface factory default.",
                IconKey = "Balanced",
                SurfaceOverlayAcIndex = NativePower.SURFACE_OVERLAY_BALANCED_AC,
                SurfaceOverlayDcIndex = NativePower.SURFACE_OVERLAY_BALANCED_DC,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BALANCED,
                SamFanProfile = 2
            };
        }

        public static CoolingProfile CreateBetterPerformance()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.BetterPerformance,
                Name = "Better Performance",
                Description = "Windows Better Performance mode, one step above Recommended.",
                IconKey = "BetterPerformance",
                SurfaceOverlayAcIndex = NativePower.SURFACE_OVERLAY_HIGH_PERF_AC,
                SurfaceOverlayDcIndex = NativePower.SURFACE_OVERLAY_HIGH_PERF_DC,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BETTER_PERFORMANCE,
                SamFanProfile = 3
            };
        }

        public static CoolingProfile CreateBestPerformance()
        {
            return new CoolingProfile
            {
                Type = CoolingProfileType.BestPerformance,
                Name = "Best Performance",
                Description = "Windows Best Performance mode, with the highest power limits.",
                IconKey = "BestPerformance",
                SurfaceOverlayAcIndex = NativePower.SURFACE_OVERLAY_MAX_PERF_AC,
                SurfaceOverlayDcIndex = NativePower.SURFACE_OVERLAY_MAX_PERF_DC,
                ProcThrottleMaxAc = 100,
                ProcThrottleMaxDc = 100,
                OverlaySchemeGuid = NativePower.GUID_OVERLAY_BEST_PERFORMANCE,
                SamFanProfile = 4
            };
        }
    }
}
