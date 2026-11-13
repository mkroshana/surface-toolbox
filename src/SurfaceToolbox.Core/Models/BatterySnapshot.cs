using System;

namespace SurfaceToolbox.Core.Models
{
    /// <summary>
    /// One reading of the battery, as reported by the Windows battery driver. A null value means
    /// the driver did not report it; nothing here is estimated or filled in.
    /// </summary>
    public class BatterySnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public bool IsOnAcPower { get; set; }
        public bool IsCharging { get; set; }
        public bool IsDischarging { get; set; }

        /// <summary>Energy currently stored, in watt-hours.</summary>
        public double? RemainingWh { get; set; }

        /// <summary>Energy the battery holds when full today, in watt-hours.</summary>
        public double? FullChargeWh { get; set; }

        /// <summary>Energy the battery held when new, in watt-hours.</summary>
        public double? DesignWh { get; set; }

        public int? CycleCount { get; set; }
        public double? VoltageV { get; set; }

        /// <summary>Power flowing in (positive, charging) or out (negative, discharging), in watts.</summary>
        public double? RateW { get; set; }

        public double? TemperatureCelsius { get; set; }

        /// <summary>The driver's own estimate of time left on battery; only reported while discharging.</summary>
        public TimeSpan? EstimatedRuntime { get; set; }

        public string Chemistry { get; set; } = string.Empty;

        public double? ChargePercent =>
            RemainingWh is double remaining && FullChargeWh is double full && full > 0
                ? Math.Clamp(remaining / full * 100.0, 0, 100)
                : null;

        /// <summary>Full-charge capacity as a share of design capacity.</summary>
        public double? HealthPercent =>
            FullChargeWh is double full && DesignWh is double design && design > 0
                ? full / design * 100.0
                : null;

        /// <summary>Time until full at the current charge rate; null unless charging at a known rate.</summary>
        public TimeSpan? TimeToFull =>
            IsCharging && RateW is double rate && rate > 0 && RemainingWh is double remaining && FullChargeWh is double full && full > remaining
                ? TimeSpan.FromHours((full - remaining) / rate)
                : null;
    }
}
