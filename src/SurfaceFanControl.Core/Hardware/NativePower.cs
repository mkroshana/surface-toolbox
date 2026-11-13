using System;
using System.Runtime.InteropServices;

namespace SurfaceFanControl.Core.Hardware
{
    /// <summary>The power source a power mode applies to. Windows keeps a separate mode for each.</summary>
    public enum PowerSource
    {
        PluggedIn,
        Battery
    }

    public static class NativePower
    {
        public const uint ERROR_NOT_SUPPORTED = 50;

        // Per-source power mode ("Plugged in" / "On battery" in Windows Settings). These exports are
        // not in the public SDK headers; they were found in powrprof.dll on Windows 11 build 26300
        // and checked there: each reads or writes only its own source's mode. Older builds may
        // lack them, so every call goes through the Try* helpers below.
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetUserConfiguredACPowerMode(out Guid PowerModeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid PowerModeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid PowerModeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid PowerModeGuid);

        /// <summary>Reads the power mode Windows will use on the given source. False if this build has no per-source call.</summary>
        public static bool TryGetConfiguredPowerMode(PowerSource source, out Guid mode)
        {
            mode = Guid.Empty;
            try
            {
                uint result = source == PowerSource.PluggedIn
                    ? PowerGetUserConfiguredACPowerMode(out mode)
                    : PowerGetUserConfiguredDCPowerMode(out mode);
                return result == ERROR_SUCCESS;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Sets the power mode for one source without touching the other. Returns a Win32 error
        /// code; ERROR_NOT_SUPPORTED if this build has no per-source call.
        /// </summary>
        public static uint SetConfiguredPowerMode(PowerSource source, Guid mode)
        {
            try
            {
                return source == PowerSource.PluggedIn
                    ? PowerSetUserConfiguredACPowerMode(ref mode)
                    : PowerSetUserConfiguredDCPowerMode(ref mode);
            }
            catch (EntryPointNotFoundException)
            {
                return ERROR_NOT_SUPPORTED;
            }
        }

        // Surface Overlay Power Settings Subgroup & Setting
        public static readonly Guid GUID_SURFACE_OVERLAY_SUBGROUP = new Guid("bfeba2e4-fdcc-41d9-93f4-3f6991c1f730");
        public static readonly Guid GUID_SURFACE_OVERLAY_SETTING  = new Guid("a4a0ab02-11f1-4584-bc52-cbb98eabdb0b");

        // Processor Power Management Subgroup & Settings
        public static readonly Guid GUID_PROCESSOR_SUBGROUP      = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid GUID_PROCTHROTTLEMAX         = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid GUID_PROCTHROTTLEMIN         = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");

        // Surface Overlay Values:
        // 1: Balanced AC
        // 2: Balanced DC
        // 3: High Performance AC
        // 4: High Performance DC
        // 5: Max Performance AC (Turbo Fan)
        // 6: Max Performance DC (Turbo Fan)
        public const uint SURFACE_OVERLAY_BALANCED_AC        = 1;
        public const uint SURFACE_OVERLAY_BALANCED_DC        = 2;
        public const uint SURFACE_OVERLAY_HIGH_PERF_AC       = 3;
        public const uint SURFACE_OVERLAY_HIGH_PERF_DC       = 4;
        public const uint SURFACE_OVERLAY_MAX_PERF_AC        = 5;
        public const uint SURFACE_OVERLAY_MAX_PERF_DC        = 6;

        // Windows 11 Modern Standby Power Slider Overlay Schemes
        public static readonly Guid GUID_OVERLAY_BEST_POWER_EFFICIENCY = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid GUID_OVERLAY_BALANCED              = Guid.Empty;
        public static readonly Guid GUID_OVERLAY_BETTER_PERFORMANCE    = new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
        public static readonly Guid GUID_OVERLAY_BEST_PERFORMANCE      = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        public const uint ERROR_SUCCESS = 0;

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetEffectiveOverlayScheme(
            out Guid EffectiveOverlayPolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerSetActiveOverlayScheme(
            ref Guid OverlaySchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerGetActiveScheme(
            IntPtr UserRootPowerKey,
            out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerSetActiveScheme(
            IntPtr UserRootPowerKey,
            ref Guid SchemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerReadDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            out uint DcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey,
            ref Guid SchemeGuid,
            ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid,
            uint DcValueIndex);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}
