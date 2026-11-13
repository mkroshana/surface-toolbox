using System;
using System.Collections.Generic;

namespace SurfaceToolbox.Core.Models
{
    /// <summary>
    /// What Windows reports about the device, its firmware and its storage. Read-only, and
    /// deliberately without serial numbers. A null or empty value means it was not reported.
    /// </summary>
    public class DeviceReport
    {
        public string Model { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Processor { get; set; } = string.Empty;
        public double? MemoryGb { get; set; }
        public string WindowsName { get; set; } = string.Empty;
        public string WindowsBuild { get; set; } = string.Empty;
        public string UefiVersion { get; set; } = string.Empty;
        public DateTime? UefiReleaseDate { get; set; }

        public List<FirmwareComponent> Firmware { get; set; } = new();
        public List<StorageDrive> Drives { get; set; } = new();
        public List<StorageVolume> Volumes { get; set; } = new();
    }

    /// <summary>One firmware component Windows knows a version for (UEFI, SAM, touch, and so on).</summary>
    public class FirmwareComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    public class StorageDrive
    {
        public string Model { get; set; } = string.Empty;

        /// <summary>For example "NVMe SSD".</summary>
        public string Kind { get; set; } = string.Empty;

        public double? SizeGb { get; set; }

        /// <summary>Windows' own verdict: Healthy, Warning or Unhealthy.</summary>
        public string Health { get; set; } = string.Empty;

        /// <summary>Share of the drive's rated life already used, 0 to 100.</summary>
        public int? WearPercent { get; set; }

        public int? TemperatureCelsius { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
    }

    public class StorageVolume
    {
        public string Letter { get; set; } = string.Empty;
        public double SizeGb { get; set; }
        public double FreeGb { get; set; }
    }
}
