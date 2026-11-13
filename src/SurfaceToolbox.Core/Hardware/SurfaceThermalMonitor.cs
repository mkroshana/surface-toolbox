using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using SurfaceToolbox.Core.Models;

namespace SurfaceToolbox.Core.Hardware
{
    public class SurfaceThermalMonitor : IDisposable
    {
        private readonly Timer _timer;
        private readonly SurfacePowerManager _powerManager;
        private int _isDisposed;
        private int _isPolling;

        public event Action<SystemThermalSnapshot>? SnapshotUpdated;

        private static readonly Dictionary<string, string> ZoneLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            { "_sb.tz01", "SoC / Package Core" },
            { "_sb.tz02", "CPU Die Sensor" },
            { "_sb.tz03", "Power Delivery / VRM" },
            { "_sb.tz04", "Memory / LPDDR" },
            { "_sb.tz05", "Battery Sensor A" },
            { "_sb.tz06", "Battery Sensor B" },
            { "_sb.tz07", "Chassis Main" },
            { "_sb.tz08", "Display Ambient" },
            { "_sb.tz09", "Wireless / Wi-Fi" },
            { "_sb.tz0a", "USB-C / Retimer" },
            { "_sb.tz0b", "Ambient Sensor" }
        };

        public SurfaceThermalMonitor(SurfacePowerManager powerManager, int pollIntervalMs = 1000)
        {
            _powerManager = powerManager;
            _timer = new Timer(OnTimerTick, null, 50, pollIntervalMs);
        }

        private void OnTimerTick(object? state)
        {
            if (Volatile.Read(ref _isDisposed) != 0 || Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
                return;

            try
            {
                if (Volatile.Read(ref _isDisposed) != 0)
                    return;

                _powerManager.MaintainFanTargetIfNeeded();

                if (Volatile.Read(ref _isDisposed) != 0)
                    return;

                var snapshot = CaptureSnapshot();

                if (Volatile.Read(ref _isDisposed) != 0)
                    return;

                SnapshotUpdated?.Invoke(snapshot);
            }
            catch
            {
                // Silently handle any transient polling exceptions
            }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        public SystemThermalSnapshot CaptureSnapshot()
        {
            var snapshot = new SystemThermalSnapshot
            {
                Timestamp = DateTime.UtcNow,
                IsDirectSamAvailable = _powerManager.IsDirectSamAvailable,
                TargetFanRpm = _powerManager.ActiveTargetRpm
            };

            double maxTemp = 0;
            double tempSum = 0;
            int count = 0;

            // 1. Read Thermal Zones via WMI / PerfCounters
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, Temperature, PercentPassiveLimit, ThrottleReasons FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                using ManagementObjectCollection results = searcher.Get();

                foreach (ManagementObject obj in results)
                {
                    using (obj)
                    {
                        string name = obj["Name"]?.ToString() ?? string.Empty;
                        if (string.Equals(name, "\\tpol", StringComparison.OrdinalIgnoreCase)) continue;

                        double rawVal = Convert.ToDouble(obj["Temperature"] ?? 0);
                        if (rawVal <= 0) continue;

                        double tempCelsius = rawVal > 1000 ? (rawVal / 10.0 - 273.15) : (rawVal - 273.15);
                        if (tempCelsius < 0 || tempCelsius > 125) continue;

                        string cleanKey = name.Replace("\\", "").ToLowerInvariant();
                        string friendlyName = ZoneLabels.TryGetValue(cleanKey, out var label) ? label : $"Thermal Zone {name}";

                        var zone = new ThermalZoneInfo
                        {
                            Id = name,
                            DisplayName = friendlyName,
                            TemperatureCelsius = Math.Round(tempCelsius, 1),
                            HighPrecisionRaw = rawVal,
                            ThrottleReasons = Convert.ToDouble(obj["ThrottleReasons"] ?? 0)
                        };

                        snapshot.Zones.Add(zone);

                        if (tempCelsius > maxTemp) maxTemp = tempCelsius;
                        tempSum += tempCelsius;
                        count++;
                    }
                }

                snapshot.Zones.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Ignore transient WMI errors
            }

            snapshot.PeakTemperatureCelsius = Math.Round(maxTemp, 1);
            snapshot.AverageTemperatureCelsius = count > 0 ? Math.Round(tempSum / count, 1) : 0;

            // 3. Read live Fan RPM & SMF Telemetry
            if (_powerManager.SamClient.TryReadFanRpm(out int fanRpm))
            {
                snapshot.FanRpm = fanRpm;
                snapshot.FanTargetState = _powerManager.EvaluateFanTarget(fanRpm);
            }
            else if (snapshot.TargetFanRpm > 0)
            {
                // Without a reading there is nothing to compare the target against yet.
                snapshot.FanTargetState = FanTargetState.Settling;
            }

            if (_powerManager.SamClient.TryReadSmfFanProfile(out int smfProf))
            {
                snapshot.SmfFanProfile = smfProf;
            }

            if (_powerManager.SamClient.TryReadPowerLimits(out double pl1W, out double pl2W))
            {
                snapshot.Pl1Watts = pl1W;
                snapshot.Pl2Watts = pl2W;
            }

            // 4. Read CPU stats
            try
            {
                using var cpuSearcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT PercentProcessorTime, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
                using ManagementObjectCollection cpuResults = cpuSearcher.Get();

                foreach (ManagementObject cpu in cpuResults)
                {
                    using (cpu)
                    {
                        snapshot.CpuPerformancePercent = Math.Clamp(Convert.ToDouble(cpu["PercentProcessorTime"] ?? 0), 0, 100);
                        snapshot.CpuFrequencyMhz = Convert.ToDouble(cpu["ProcessorFrequency"] ?? 0);
                        break;
                    }
                }
            }
            catch { }

            // 5. Power status
            _powerManager.GetPowerStatus(out bool pluggedIn, out int battery);
            snapshot.IsPluggedIn = pluggedIn;
            snapshot.BatteryPercentage = battery;

            return snapshot;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;

            _timer.Dispose();
        }
    }
}
