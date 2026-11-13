using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using SurfaceFanControl.Core.Models;

namespace SurfaceFanControl.Core.Hardware
{
    /// <summary>
    /// Builds a <see cref="DeviceReport"/> from standard WMI classes. Read-only. Each section is
    /// read on its own, so one that fails leaves the others intact. Takes a second or two, so
    /// call it off the UI thread.
    /// </summary>
    public sealed class SurfaceDeviceReportReader
    {
        private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;

        public DeviceReport Read()
        {
            var report = new DeviceReport();
            Try(() => ReadSystem(report));
            Try(() => ReadFirmware(report));
            Try(() => ReadDrives(report));
            Try(() => ReadVolumes(report));
            return report;
        }

        private static void Try(Action section)
        {
            try { section(); }
            catch { /* That section stays empty and is shown as not reported. */ }
        }

        private static void ReadSystem(DeviceReport report)
        {
            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT Model, SystemSKUNumber, TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                using (o)
                {
                    report.Model = Text(o["Model"]);
                    report.Sku = Text(o["SystemSKUNumber"]);
                    if (o["TotalPhysicalMemory"] != null)
                        report.MemoryGb = Math.Round(Convert.ToDouble(o["TotalPhysicalMemory"]) / BytesPerGb, 1);
                }
            }

            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT Name FROM Win32_Processor"))
            {
                using (o) { report.Processor = Text(o["Name"]); }
                break;
            }

            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT Caption, BuildNumber FROM Win32_OperatingSystem"))
            {
                using (o)
                {
                    report.WindowsName = Text(o["Caption"]).Replace("Microsoft ", string.Empty);
                    report.WindowsBuild = Text(o["BuildNumber"]);
                }
            }

            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
            {
                using (o)
                {
                    report.UefiVersion = Text(o["SMBIOSBIOSVersion"]);
                    string date = Text(o["ReleaseDate"]);
                    if (date.Length >= 8)
                        report.UefiReleaseDate = ManagementDateTimeConverter.ToDateTime(date).Date;
                }
            }
        }

        // Driver dates are deliberately not reported: on the Surface Pro 9 several of them are
        // plainly not real release dates (one was in the future), so only versions are shown.
        private static void ReadFirmware(DeviceReport report)
        {
            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT DeviceName, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass = 'FIRMWARE'"))
            {
                using (o)
                {
                    string name = Text(o["DeviceName"]);
                    string version = Text(o["DriverVersion"]);
                    if (name.Length > 0 && version.Length > 0)
                        report.Firmware.Add(new FirmwareComponent { Name = name, Version = version });
                }
            }
            report.Firmware = report.Firmware.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ReadDrives(DeviceReport report)
        {
            const string ns = "root\\Microsoft\\Windows\\Storage";
            foreach (ManagementObject disk in Query(ns, "SELECT * FROM MSFT_PhysicalDisk"))
            {
                using (disk)
                {
                    var drive = new StorageDrive
                    {
                        Model = Text(disk["FriendlyName"]),
                        Kind = DescribeKind(ToInt(disk["BusType"]), ToInt(disk["MediaType"])),
                        Health = DescribeHealth(ToInt(disk["HealthStatus"])),
                        FirmwareVersion = Text(disk["FirmwareVersion"])
                    };
                    if (disk["Size"] != null)
                        drive.SizeGb = Math.Round(Convert.ToDouble(disk["Size"]) / BytesPerGb, 0);

                    // Wear and temperature come from the drive's SMART data, through an association.
                    try
                    {
                        using ManagementObjectCollection counters = disk.GetRelated("MSFT_StorageReliabilityCounter");
                        foreach (ManagementObject counter in counters)
                        {
                            using (counter)
                            {
                                drive.WearPercent = ToInt(counter["Wear"]);
                                int? temperature = ToInt(counter["Temperature"]);
                                if (temperature is > 0)
                                    drive.TemperatureCelsius = temperature;
                            }
                            break;
                        }
                    }
                    catch
                    {
                        // The drive does not expose reliability counters; health and size still stand.
                    }

                    report.Drives.Add(drive);
                }
            }
        }

        private static void ReadVolumes(DeviceReport report)
        {
            foreach (ManagementObject o in Query("root\\CIMV2", "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3"))
            {
                using (o)
                {
                    if (o["Size"] == null || o["FreeSpace"] == null) continue;
                    report.Volumes.Add(new StorageVolume
                    {
                        Letter = Text(o["DeviceID"]),
                        SizeGb = Math.Round(Convert.ToDouble(o["Size"]) / BytesPerGb, 0),
                        FreeGb = Math.Round(Convert.ToDouble(o["FreeSpace"]) / BytesPerGb, 0)
                    });
                }
            }
        }

        // Materialised, so the searcher and its result collection can be released straight away.
        private static List<ManagementObject> Query(string scope, string query)
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using ManagementObjectCollection results = searcher.Get();
            return results.Cast<ManagementObject>().ToList();
        }

        private static string Text(object? value) => value?.ToString()?.Trim() ?? string.Empty;

        private static int? ToInt(object? value) => value == null ? null : Convert.ToInt32(value);

        private static string DescribeKind(int? busType, int? mediaType)
        {
            string bus = busType switch
            {
                17 => "NVMe",
                11 => "SATA",
                7 => "USB",
                12 => "SD",
                10 => "SAS",
                _ => string.Empty
            };
            string media = mediaType switch
            {
                4 => "SSD",
                3 => "HDD",
                5 => "SCM",
                _ => string.Empty
            };
            string kind = $"{bus} {media}".Trim();
            return kind.Length > 0 ? kind : "Not reported";
        }

        private static string DescribeHealth(int? healthStatus) => healthStatus switch
        {
            0 => "Healthy",
            1 => "Warning",
            2 => "Unhealthy",
            _ => "Not reported"
        };
    }
}
