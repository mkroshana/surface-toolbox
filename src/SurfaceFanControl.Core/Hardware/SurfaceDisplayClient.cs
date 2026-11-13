using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using SurfaceFanControl.Core.Models;

namespace SurfaceFanControl.Core.Hardware
{
    /// <summary>
    /// Reads and changes the primary display's refresh rate (EnumDisplaySettings /
    /// ChangeDisplaySettingsEx) and the built-in panel's brightness (root\WMI
    /// WmiMonitorBrightness). Both are standard Windows interfaces, not Surface-specific.
    /// Display settings belong to the signed-in desktop session, so this only does anything
    /// useful when called from a process running there.
    /// </summary>
    public sealed class SurfaceDisplayClient
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint DM_PELSWIDTH = 0x00080000;
        private const uint DM_PELSHEIGHT = 0x00100000;
        private const uint DM_DISPLAYFREQUENCY = 0x00400000;
        private const uint CDS_UPDATEREGISTRY = 0x00000001;
        private const uint CDS_TEST = 0x00000002;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsExW(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private static DEVMODE NewDevMode()
        {
            return new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        }

        /// <summary>Reads the current mode, the refresh rates on offer, and the brightness. Null if no display mode can be read.</summary>
        public DisplayState? Read()
        {
            DEVMODE current = NewDevMode();
            if (!EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref current))
                return null;

            var state = new DisplayState
            {
                Width = (int)current.dmPelsWidth,
                Height = (int)current.dmPelsHeight,
                RefreshRateHz = (int)current.dmDisplayFrequency
            };

            // Every mode Windows lists; keep the refresh rates that go with the current resolution and colour depth.
            var rates = new SortedSet<int>();
            DEVMODE mode = NewDevMode();
            for (int i = 0; EnumDisplaySettingsW(null, i, ref mode); i++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight &&
                    mode.dmBitsPerPel == current.dmBitsPerPel && mode.dmDisplayFrequency > 1)
                {
                    rates.Add((int)mode.dmDisplayFrequency);
                }
            }
            state.AvailableRefreshRatesHz = new List<int>(rates);
            state.BrightnessPercent = ReadBrightness();
            return state;
        }

        /// <summary>
        /// Switches the primary display to the given refresh rate at its current resolution, and
        /// has Windows remember it. Returns false if Windows rejects the mode.
        /// </summary>
        public bool SetRefreshRate(int hz)
        {
            DEVMODE mode = NewDevMode();
            if (!EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode))
                return false;

            if (mode.dmDisplayFrequency == (uint)hz)
                return true;

            mode.dmDisplayFrequency = (uint)hz;
            mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            // Ask first, so an unsupported rate is refused without the screen blanking.
            if (ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL)
                return false;

            return ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
        }

        /// <summary>The built-in panel's brightness, 0 to 100, or null if it does not expose one.</summary>
        public int? ReadBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
                using ManagementObjectCollection results = searcher.Get();
                foreach (ManagementObject monitor in results)
                {
                    using (monitor)
                    {
                        return Convert.ToInt32(monitor["CurrentBrightness"]);
                    }
                }
            }
            catch
            {
                // No WMI brightness interface (external monitor only, or the driver does not provide one).
            }
            return null;
        }

        /// <summary>Sets the built-in panel's brightness, 0 to 100.</summary>
        public bool SetBrightness(int percent)
        {
            byte level = (byte)Math.Clamp(percent, 0, 100);
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
                using ManagementObjectCollection results = searcher.Get();
                foreach (ManagementObject monitor in results)
                {
                    using (monitor)
                    {
                        // WmiSetBrightness(Timeout in seconds, Brightness in percent)
                        monitor.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, level });
                        return true;
                    }
                }
            }
            catch
            {
                // Reported to the caller as a failed change.
            }
            return false;
        }
    }
}
