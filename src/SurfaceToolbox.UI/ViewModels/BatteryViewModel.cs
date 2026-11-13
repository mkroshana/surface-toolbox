using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using SurfaceToolbox.Core.Hardware;
using SurfaceToolbox.Core.Models;

namespace SurfaceToolbox.UI.ViewModels
{
    /// <summary>
    /// Backs the Battery page. Read-only: it polls the battery driver while the page is open and
    /// formats what the driver reports. A value the driver does not report is shown as
    /// "Not reported" rather than estimated.
    /// </summary>
    public class BatteryViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string NotReported = "Not reported";
        private const int PollIntervalMs = 5000;

        private readonly SurfaceBatteryClient _client = new();
        private readonly object _timerLock = new();
        private System.Threading.Timer? _timer;
        private BatterySnapshot? _snapshot;
        private bool _hasRead;
        private int _isReading;
        private volatile bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Starts polling. Called when the page becomes visible.</summary>
        public void Start()
        {
            lock (_timerLock)
            {
                if (_disposed || _timer != null) return;
                _timer = new System.Threading.Timer(OnTick, null, 0, PollIntervalMs);
            }
        }

        /// <summary>Stops polling. Called when the page is hidden, so nothing is read in the background.</summary>
        public void Stop()
        {
            lock (_timerLock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private void OnTick(object? state)
        {
            if (_disposed || Interlocked.CompareExchange(ref _isReading, 1, 0) != 0)
                return;

            try
            {
                BatterySnapshot? snapshot = _client.Read();
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    _snapshot = snapshot;
                    _hasRead = true;
                    // Every display property derives from the snapshot, so refresh them all.
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
                }, DispatcherPriority.Background);
            }
            catch
            {
                // A failed read leaves the last values on screen; the next tick tries again.
            }
            finally
            {
                Interlocked.Exchange(ref _isReading, 0);
            }
        }

        public string ChargePercentText => _snapshot?.ChargePercent is double percent ? $"{percent:F0}%" : NotReported;

        public string StateText
        {
            get
            {
                if (_snapshot == null)
                    return _hasRead ? "No battery found" : "Reading battery...";

                if (_snapshot.IsCharging)
                    return _snapshot.RateW is double chargeRate && chargeRate > 0 ? $"Charging at {chargeRate:F1} W" : "Charging";

                if (_snapshot.IsDischarging)
                    return _snapshot.RateW is double drain && drain < 0 ? $"On battery, drawing {-drain:F1} W" : "On battery";

                if (_snapshot.IsOnAcPower)
                    return _snapshot.ChargePercent is double percent && percent >= 99.5 ? "Plugged in, fully charged" : "Plugged in, not charging";

                return "Idle";
            }
        }

        public string TimeText
        {
            get
            {
                if (_snapshot?.IsDischarging == true && _snapshot.EstimatedRuntime is TimeSpan runtime)
                    return FormatDuration(runtime);
                if (_snapshot?.TimeToFull is TimeSpan toFull)
                    return FormatDuration(toFull);
                return NotReported;
            }
        }

        public string TimeCaption
        {
            get
            {
                if (_snapshot?.IsDischarging == true) return "Estimated Time Remaining";
                if (_snapshot?.IsCharging == true) return "Estimated Time Until Full";
                return "Time Estimate";
            }
        }

        public string HealthText => _snapshot?.HealthPercent is double health ? $"{health:F1}%" : NotReported;

        public string HealthDetailText =>
            _snapshot?.FullChargeWh is double full && _snapshot.DesignWh is double design
                ? $"{full:F2} of {design:F2} Wh design capacity"
                : "Design capacity not reported";

        public string CycleCountText => _snapshot?.CycleCount is int cycles ? cycles.ToString() : NotReported;

        public string PowerFlowText
        {
            get
            {
                if (_snapshot?.RateW is not double rate) return NotReported;
                if (rate > 0) return $"{rate:F1} W in";
                if (rate < 0) return $"{-rate:F1} W out";
                return "0.0 W";
            }
        }

        public string VoltageText => _snapshot?.VoltageV is double voltage ? $"{voltage:F2} V" : NotReported;
        public string RemainingText => FormatWattHours(_snapshot?.RemainingWh);
        public string FullChargeText => FormatWattHours(_snapshot?.FullChargeWh);
        public string DesignText => FormatWattHours(_snapshot?.DesignWh);

        public string CapacityLostText =>
            _snapshot?.FullChargeWh is double full && _snapshot.DesignWh is double design
                ? $"{Math.Max(0, design - full):F2} Wh"
                : NotReported;

        public string TemperatureText => _snapshot?.TemperatureCelsius is double temperature ? $"{temperature:F1} °C" : NotReported;

        public string ChemistryText
        {
            get
            {
                string code = _snapshot?.Chemistry ?? string.Empty;
                return code.ToUpperInvariant() switch
                {
                    "" => NotReported,
                    "LION" or "LI-I" => "Lithium-ion",
                    "LIP" => "Lithium polymer",
                    "NIMH" => "Nickel-metal hydride",
                    "NICD" => "Nickel-cadmium",
                    "PBAC" => "Lead-acid",
                    _ => code
                };
            }
        }

        public string LastUpdatedText => _snapshot != null ? $"Updated {_snapshot.Timestamp.ToLocalTime():HH:mm:ss}" : string.Empty;

        // Two decimals, so the figures line up with the milliwatt-hours Windows' own battery report shows.
        private static string FormatWattHours(double? wattHours) => wattHours is double value ? $"{value:F2} Wh" : NotReported;

        private static string FormatDuration(TimeSpan duration)
        {
            int hours = (int)duration.TotalHours;
            return hours > 0 ? $"{hours} h {duration.Minutes:00} min" : $"{duration.Minutes} min";
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
        }
    }
}
