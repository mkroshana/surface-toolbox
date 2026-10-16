using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using SurfaceFanControl.Core.Hardware;
using SurfaceFanControl.Core.Models;
using SurfaceFanControl.Core.Profiles;

namespace SurfaceFanControl.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SurfacePowerManager _powerManager;
        private readonly SurfaceThermalMonitor _thermalMonitor;
        private readonly ProfileManager _profileManager;
        private readonly SurfaceDeviceInfo _deviceInfo;

        private double _currentPeakTemp = 36.5;
        private double _currentAvgTemp = 35.8;
        private double _cpuFrequency = 2200;
        private double _cpuPerformance = 15;
        private int _currentFanRpm;
        private int _targetFanRpm;
        private int _manualSliderRpm = 3400;
        private int _smfFanProfile = 2;
        private double _pl1Watts = 21;
        private double _pl2Watts = 28;
        private bool _isDirectSamAvailable;
        private string _powerSourceText = "Detecting...";
        private string _statusMessage;
        private CoolingProfile _activeProfile;
        private bool _isStockRestored;
        private bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _powerManager = new SurfacePowerManager();
            _thermalMonitor = new SurfaceThermalMonitor(_powerManager, 1000);
            _profileManager = new ProfileManager(_powerManager);
            _deviceInfo = SurfaceDeviceDetector.Detect();

            _activeProfile = _profileManager.ActiveProfile;
            _isDirectSamAvailable = _powerManager.IsDirectSamAvailable;
            _statusMessage = _isDirectSamAvailable
                ? "Direct SAM Hardware Fan Control Active"
                : "SMF Overlay Control Active (Run as Administrator for Direct RPM Control)";

            Zones = new ObservableCollection<ThermalZoneInfo>();

            SelectQuietCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.Quiet));
            SelectBalancedCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.Balanced));
            SelectTurboCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.Turbo));
            ApplyManualRpmCommand = new RelayCommand(_ => ApplyManualRpm());
            RestoreStockCommand = new RelayCommand(_ => RestoreStock());

            _thermalMonitor.SnapshotUpdated += OnSnapshotUpdated;
            _profileManager.ActiveProfileChanged += OnProfileChanged;
        }

        public string DeviceName => string.IsNullOrWhiteSpace(_deviceInfo.Model) ? "Surface Pro 9" : _deviceInfo.Model;
        public string ProcessorName => string.IsNullOrWhiteSpace(_deviceInfo.ProcessorName) ? "Intel Core Processor" : _deviceInfo.ProcessorName;

        public double CurrentPeakTemp
        {
            get => _currentPeakTemp;
            set { if (SetField(ref _currentPeakTemp, value)) OnPropertyChanged(nameof(CurrentPeakTempFormatted)); }
        }

        public string CurrentPeakTempFormatted => $"{_currentPeakTemp:F1} °C";

        public double CurrentAvgTemp
        {
            get => _currentAvgTemp;
            set { if (SetField(ref _currentAvgTemp, value)) OnPropertyChanged(nameof(CurrentAvgTempFormatted)); }
        }

        public string CurrentAvgTempFormatted => $"{_currentAvgTemp:F1} °C";

        public int CurrentFanRpm
        {
            get => _currentFanRpm;
            set
            {
                if (SetField(ref _currentFanRpm, value))
                {
                    OnPropertyChanged(nameof(CurrentFanRpmFormatted));
                    OnPropertyChanged(nameof(FanStateSubtitle));
                }
            }
        }

        public string CurrentFanRpmFormatted => $"{_currentFanRpm} RPM";

        public int TargetFanRpm
        {
            get => _targetFanRpm;
            set
            {
                if (SetField(ref _targetFanRpm, value))
                {
                    OnPropertyChanged(nameof(FanStateSubtitle));
                }
            }
        }

        public int SmfFanProfile
        {
            get => _smfFanProfile;
            set
            {
                if (SetField(ref _smfFanProfile, value))
                {
                    OnPropertyChanged(nameof(FanStateSubtitle));
                }
            }
        }

        public string FanStateSubtitle => _targetFanRpm > 0
            ? $"Target {_targetFanRpm} RPM (SAM Direct)"
            : $"Auto Curve (SMF Profile {_smfFanProfile})";

        public int ManualSliderRpm
        {
            get => _manualSliderRpm;
            set
            {
                int snapped = (int)(Math.Round(value / 100.0) * 100);
                if (SetField(ref _manualSliderRpm, snapped))
                {
                    OnPropertyChanged(nameof(ManualSliderRpmFormatted));
                }
            }
        }

        public string ManualSliderRpmFormatted => _manualSliderRpm > 0 ? $"{_manualSliderRpm} RPM" : "Auto (0 RPM)";

        public double Pl1Watts
        {
            get => _pl1Watts;
            set { if (SetField(ref _pl1Watts, value)) OnPropertyChanged(nameof(PowerLimitsFormatted)); }
        }

        public double Pl2Watts
        {
            get => _pl2Watts;
            set { if (SetField(ref _pl2Watts, value)) OnPropertyChanged(nameof(PowerLimitsFormatted)); }
        }

        public string PowerLimitsFormatted => $"{_pl1Watts:F0}W / {_pl2Watts:F0}W";

        public double CpuFrequency
        {
            get => _cpuFrequency;
            set { if (SetField(ref _cpuFrequency, value)) OnPropertyChanged(nameof(CpuFrequencyFormatted)); }
        }

        public string CpuFrequencyFormatted => $"{_cpuFrequency:F0} MHz";

        public double CpuPerformance
        {
            get => _cpuPerformance;
            set { if (SetField(ref _cpuPerformance, value)) OnPropertyChanged(nameof(CpuPerformanceFormatted)); }
        }

        public string CpuPerformanceFormatted => $"{_cpuPerformance:F0}%";

        public string PowerSourceText
        {
            get => _powerSourceText;
            set => SetField(ref _powerSourceText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public bool IsDirectSamAvailable
        {
            get => _isDirectSamAvailable;
            set
            {
                if (SetField(ref _isDirectSamAvailable, value))
                {
                    OnPropertyChanged(nameof(HardwareModeBadgeText));
                }
            }
        }

        public string HardwareModeBadgeText => _isDirectSamAvailable ? "SAM Direct + SMF" : "SMF Overlay Only";

        public CoolingProfile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (SetField(ref _activeProfile, value))
                {
                    OnPropertyChanged(nameof(IsQuietActive));
                    OnPropertyChanged(nameof(IsBalancedActive));
                    OnPropertyChanged(nameof(IsTurboActive));
                    OnPropertyChanged(nameof(IsManualActive));
                }
            }
        }

        public bool IsQuietActive => ActiveProfile.Type == CoolingProfileType.Quiet;
        public bool IsBalancedActive => ActiveProfile.Type == CoolingProfileType.Balanced;
        public bool IsTurboActive => ActiveProfile.Type == CoolingProfileType.Turbo;
        public bool IsManualActive => ActiveProfile.Type == CoolingProfileType.Manual;

        public bool IsStockRestored
        {
            get => _isStockRestored;
            set => SetField(ref _isStockRestored, value);
        }

        public ObservableCollection<ThermalZoneInfo> Zones { get; }

        public ICommand SelectQuietCommand { get; }
        public ICommand SelectBalancedCommand { get; }
        public ICommand SelectTurboCommand { get; }
        public ICommand ApplyManualRpmCommand { get; }
        public ICommand RestoreStockCommand { get; }

        public ProfileManager ProfileManager => _profileManager;

        public void SetProfile(CoolingProfileType type)
        {
            bool ok = _profileManager.SetProfile(type);
            if (ok)
            {
                var active = _profileManager.ActiveProfile;
                if (active.TargetFanRpm > 0 && !_powerManager.IsDirectSamAvailable)
                {
                    StatusMessage = $"Switched to {active.Name} (SMF Profile {active.SamFanProfile} applied; run as Admin for {active.TargetFanRpm} RPM override)";
                }
                else if (active.TargetFanRpm > 0)
                {
                    StatusMessage = $"Switched to {active.Name} — Commanding {active.TargetFanRpm} RPM via SAM";
                }
                else
                {
                    StatusMessage = $"Switched to {active.Name} (SMF Fan Profile {active.SamFanProfile})";
                }
                IsStockRestored = false;
            }
            else
            {
                StatusMessage = "Failed to switch profile.";
            }
        }

        public void ApplyManualRpm()
        {
            ushort target = (ushort)Math.Clamp(_manualSliderRpm, 0, 5000);
            bool ok = _profileManager.SetManualRpm(target);
            if (ok)
            {
                StatusMessage = _powerManager.IsDirectSamAvailable
                    ? (target > 0
                        ? $"Manual Fan Speed Set to {target} RPM (SAM Direct)"
                        : "Fan Returned to Automatic SAM Curve (0 RPM Override)")
                    : "Administrator privileges required for direct SAM RPM override.";
                IsStockRestored = false;
            }
            else
            {
                StatusMessage = "Failed to apply manual fan speed.";
            }
        }

        public void RestoreStock()
        {
            bool ok = _profileManager.RestoreStock();
            if (ok)
            {
                StatusMessage = "OEM Stock Settings & Automatic SAM Fan Curve Restored";
                IsStockRestored = true;
            }
            else
            {
                StatusMessage = "Failed to restore stock settings.";
            }
        }

        private void OnSnapshotUpdated(SystemThermalSnapshot snapshot)
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                CurrentPeakTemp = snapshot.PeakTemperatureCelsius;
                CurrentAvgTemp = snapshot.AverageTemperatureCelsius;
                CpuFrequency = snapshot.CpuFrequencyMhz;
                CpuPerformance = snapshot.CpuPerformancePercent;
                CurrentFanRpm = snapshot.FanRpm;
                TargetFanRpm = snapshot.TargetFanRpm;
                SmfFanProfile = snapshot.SmfFanProfile;
                Pl1Watts = snapshot.Pl1Watts;
                Pl2Watts = snapshot.Pl2Watts;
                IsDirectSamAvailable = snapshot.IsDirectSamAvailable;

                PowerSourceText = snapshot.IsPluggedIn
                    ? $"Plugged In (AC) • {snapshot.BatteryPercentage}%"
                    : $"On Battery (DC) • {snapshot.BatteryPercentage}%";

                if (Zones.Count != snapshot.Zones.Count)
                {
                    Zones.Clear();
                    foreach (var z in snapshot.Zones)
                    {
                        Zones.Add(z);
                    }
                }
                else
                {
                    for (int i = 0; i < Zones.Count; i++)
                    {
                        Zones[i].TemperatureCelsius = snapshot.Zones[i].TemperatureCelsius;
                        Zones[i].ThrottleReasons = snapshot.Zones[i].ThrottleReasons;
                    }
                }
            }, DispatcherPriority.Background);
        }

        private void OnProfileChanged(CoolingProfile profile)
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                ActiveProfile = profile;
                TargetFanRpm = profile.TargetFanRpm;
            });
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _thermalMonitor.SnapshotUpdated -= OnSnapshotUpdated;
            _profileManager.ActiveProfileChanged -= OnProfileChanged;
            _thermalMonitor.Dispose();
            _powerManager.Dispose();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
