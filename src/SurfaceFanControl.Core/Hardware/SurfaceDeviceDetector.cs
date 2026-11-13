using System;
using System.Management;
using System.Runtime.InteropServices;

namespace SurfaceFanControl.Core.Hardware
{
    public class SurfaceDeviceInfo
    {
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string ProcessorName { get; set; } = string.Empty;
        public bool IsSurfaceDevice { get; set; }
        public bool HasSurfaceOverlaySupport { get; set; }
    }

    public static class SurfaceDeviceDetector
    {
        public static SurfaceDeviceInfo Detect()
        {
            var info = new SurfaceDeviceInfo();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    using (obj)
                    {
                        info.Manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                        info.Model = obj["Model"]?.ToString() ?? string.Empty;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                using var cpuResults = cpuSearcher.Get();
                foreach (ManagementObject obj in cpuResults)
                {
                    using (obj)
                    {
                        info.ProcessorName = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                        break;
                    }
                }
            }
            catch { }

            // Both must match: Hyper-V guests also report "Microsoft Corporation" as the manufacturer.
            info.IsSurfaceDevice = info.Manufacturer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                                  info.Model.Contains("Surface", StringComparison.OrdinalIgnoreCase);

            // Check if Surface Overlay power setting can be read
            Guid scheme = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
            if (NativePower.PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) == NativePower.ERROR_SUCCESS && ptr != IntPtr.Zero)
            {
                try
                {
                    scheme = Marshal.PtrToStructure<Guid>(ptr);
                }
                finally
                {
                    NativePower.LocalFree(ptr);
                }
            }

            Guid overlaySubgroup = NativePower.GUID_SURFACE_OVERLAY_SUBGROUP;
            Guid overlaySetting = NativePower.GUID_SURFACE_OVERLAY_SETTING;

            uint ret = NativePower.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref overlaySubgroup, ref overlaySetting, out _);
            info.HasSurfaceOverlaySupport = (ret == NativePower.ERROR_SUCCESS);

            return info;
        }
    }
}
