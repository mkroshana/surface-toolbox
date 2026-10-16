using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SurfaceFanControl.Core.Models
{
    public class ThermalZoneInfo : INotifyPropertyChanged
    {
        private double _temperatureCelsius;
        private double _throttleReasons;

        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public double TemperatureCelsius
        {
            get => _temperatureCelsius;
            set
            {
                if (Math.Abs(_temperatureCelsius - value) > 0.01)
                {
                    _temperatureCelsius = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedTemperature));
                }
            }
        }

        public double HighPrecisionRaw { get; set; }

        public double ThrottleReasons
        {
            get => _throttleReasons;
            set
            {
                if (Math.Abs(_throttleReasons - value) > 0.01)
                {
                    _throttleReasons = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FormattedTemperature => $"{TemperatureCelsius:F1} °C";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SystemThermalSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<ThermalZoneInfo> Zones { get; set; } = new();
        public double PeakTemperatureCelsius { get; set; }
        public double AverageTemperatureCelsius { get; set; }
        public double CpuPerformancePercent { get; set; }
        public double CpuFrequencyMhz { get; set; }
        public bool IsPluggedIn { get; set; }
        public int BatteryPercentage { get; set; }
        public int FanRpm { get; set; }
        public int TargetFanRpm { get; set; }
        public int SmfFanProfile { get; set; } = 2;
        public double Pl1Watts { get; set; }
        public double Pl2Watts { get; set; }
        public bool IsDirectSamAvailable { get; set; }
    }
}
