using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using SurfaceToolbox.Core.Hardware;
using SurfaceToolbox.Core.Models;
using SurfaceToolbox.Core.Profiles;

namespace SurfaceToolbox.UI.ViewModels
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
        private FanTargetState _fanTargetState;
        private double _pl1Watts = 13;
        private double _pl2Watts = 28;
        private bool _isDirectSamAvailable;
        private string _powerSourceText = "Detecting...";
        private string _statusMessage;
        private CoolingProfile _activeProfile;
        private PowerSource _sourceInUse;
        private PowerSource _editingSource;
        private bool _isStockRestored;
        private volatile bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel(SurfaceDeviceInfo? deviceInfo = null)
        {
            Zones = new ObservableCollection<ThermalZoneInfo>();

            _powerManager = new SurfacePowerManager();
            _profileManager = new ProfileManager(_powerManager);
            _deviceInfo = deviceInfo ?? SurfaceDeviceDetector.Detect();

            _sourceInUse = _powerManager.CurrentPowerSource;
            _editingSource = _sourceInUse;
            _activeProfile = _profileManager.ActiveProfile;
            _isDirectSamAvailable = _powerManager.IsDirectSamAvailable;
            _targetFanRpm = _powerManager.ActiveTargetRpm;
            _statusMessage = _isDirectSamAvailable
                ? "Direct SAM Hardware Fan Control Active"
                : "SMF Overlay Control Active (Run as Administrator for Direct RPM Control)";

            SelectQuietCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.Quiet, _editingSource));
            SelectBalancedCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.Balanced, _editingSource));
            SelectBetterPerformanceCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.BetterPerformance, _editingSource));
            SelectBestPerformanceCommand = new RelayCommand(_ => SetProfile(CoolingProfileType.BestPerformance, _editingSource));
            ApplyManualRpmCommand = new RelayCommand(_ => ApplyManualRpm());
            ResetFanOnlyCommand = new RelayCommand(_ => ResetFanOnly());
            RestoreStockCommand = new RelayCommand(_ => RestoreStock());
            OpenGitHubCommand = new RelayCommand(_ => OpenExternalLink("https://github.com/mkroshana"));
            OpenEmailCommand = new RelayCommand(_ => OpenExternalLink("mailto:mkroshana@gmail.com"));

            _profileManager.ActiveProfileChanged += OnProfileChanged;
            _thermalMonitor = new SurfaceThermalMonitor(_powerManager, 1000);
            _thermalMonitor.SnapshotUpdated += OnSnapshotUpdated;
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

        public string FanStateSubtitle => _isDirectSamAvailable && _targetFanRpm > 0
            ? _fanTargetState switch
            {
                FanTargetState.HeldAboveTarget => $"Target {_targetFanRpm} RPM (EC Thermal Floor)",
                FanTargetState.NotApplied => $"Target {_targetFanRpm} RPM (Not Applied)",
                _ => $"Target {_targetFanRpm} RPM (SAM Direct)"
            }
            : $"Auto Curve (SMF Profile {_smfFanProfile})";

        public int ManualSliderRpm
        {
            get => _manualSliderRpm;
            set
            {
                int rounded = (int)(Math.Round(value / 100.0) * 100);
                int normalized;
                if (rounded <= 0)
                {
                    normalized = 0;
                }
                else if (rounded < SurfaceSamClient.MinManualFanRpm)
                {
                    // When dragging up from 0, jump to MinManualFanRpm (2000 RPM).
                    // When dragging down below 2000 RPM, snap to 0 if <= 1000, else 2000 RPM.
                    normalized = _manualSliderRpm == 0 || rounded > 1000
                        ? SurfaceSamClient.MinManualFanRpm
                        : 0;
                }
                else
                {
                    normalized = Math.Min(rounded, (int)SurfaceSamClient.MaxManualFanRpm);
                }

                if (SetField(ref _manualSliderRpm, normalized))
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
                    OnPropertyChanged(nameof(FanStateSubtitle));
                }
            }
        }

        public string HardwareModeBadgeText => _isDirectSamAvailable ? "SAM Direct + SMF" : "SMF Overlay Only";

        /// <summary>The power mode of the power source in use; shown in the header badge and tray.</summary>
        public CoolingProfile ActiveProfile
        {
            get => _activeProfile;
            set => SetField(ref _activeProfile, value);
        }

        // Windows keeps one power mode for "plugged in" and one for "on battery". The Power Mode
        // cards show and change the mode of whichever source is selected here.
        public bool IsEditingPluggedIn
        {
            get => _editingSource == PowerSource.PluggedIn;
            set { if (value) SetEditingSource(PowerSource.PluggedIn); }
        }

        public bool IsEditingBattery
        {
            get => _editingSource == PowerSource.Battery;
            set { if (value) SetEditingSource(PowerSource.Battery); }
        }

        public string PluggedInSourceLabel => _sourceInUse == PowerSource.PluggedIn ? "Plugged In (In Use)" : "Plugged In";
        public string BatterySourceLabel => _sourceInUse == PowerSource.Battery ? "On Battery (In Use)" : "On Battery";

        private CoolingProfile EditingProfile => _profileManager.GetActiveProfile(_editingSource);

        public bool IsQuietActive => EditingProfile.Type == CoolingProfileType.Quiet;
        public bool IsBalancedActive => EditingProfile.Type == CoolingProfileType.Balanced || EditingProfile.Type == CoolingProfileType.StockReset;
        public bool IsBetterPerformanceActive => EditingProfile.Type == CoolingProfileType.BetterPerformance;
        public bool IsBestPerformanceActive => EditingProfile.Type == CoolingProfileType.BestPerformance;

        private void SetEditingSource(PowerSource source)
        {
            if (_editingSource == source) return;
            _editingSource = source;
            OnPropertyChanged(nameof(IsEditingPluggedIn));
            OnPropertyChanged(nameof(IsEditingBattery));
            RaiseModeFlagsChanged();
        }

        private void RaiseModeFlagsChanged()
        {
            OnPropertyChanged(nameof(IsQuietActive));
            OnPropertyChanged(nameof(IsBalancedActive));
            OnPropertyChanged(nameof(IsBetterPerformanceActive));
            OnPropertyChanged(nameof(IsBestPerformanceActive));
        }

        // Called on every reading. When the device is plugged in or unplugged, Windows switches
        // to the other source's power mode by itself; follow it in the header and the selector.
        private void UpdateSourceInUse(PowerSource source)
        {
            if (_sourceInUse == source) return;
            _sourceInUse = source;
            OnPropertyChanged(nameof(PluggedInSourceLabel));
            OnPropertyChanged(nameof(BatterySourceLabel));
            ActiveProfile = _profileManager.GetActiveProfile(source);
            SetEditingSource(source);
        }

        private static string DescribeSource(PowerSource source) => source == PowerSource.PluggedIn ? "Plugged In" : "On Battery";

        public bool IsStockRestored
        {
            get => _isStockRestored;
            set => SetField(ref _isStockRestored, value);
        }

        public ObservableCollection<ThermalZoneInfo> Zones { get; }

        /// <summary>Backs the Battery page; it only polls while that page is on screen.</summary>
        public BatteryViewModel Battery { get; } = new BatteryViewModel();

        /// <summary>Backs the Display page; it only polls while that page is on screen.</summary>
        public DisplayViewModel Display { get; } = new DisplayViewModel();

        /// <summary>Backs the History page; every monitor reading is recorded into it.</summary>
        public HistoryViewModel History { get; } = new HistoryViewModel();

        /// <summary>Backs the Device page; the report is read when that page is first opened.</summary>
        public DeviceViewModel Device { get; } = new DeviceViewModel();

        /// <summary>Backs the Diagnostics page.</summary>
        public DiagnosticsViewModel Diagnostics { get; } = new DiagnosticsViewModel();

        /// <summary>Backs the Settings page.</summary>
        public SettingsViewModel Settings { get; } = new SettingsViewModel();

        public ICommand SelectQuietCommand { get; }
        public ICommand SelectBalancedCommand { get; }
        public ICommand SelectBetterPerformanceCommand { get; }
        public ICommand SelectBestPerformanceCommand { get; }
        public ICommand ApplyManualRpmCommand { get; }
        public ICommand ResetFanOnlyCommand { get; }
        public ICommand RestoreStockCommand { get; }
        public ICommand OpenGitHubCommand { get; }
        public ICommand OpenEmailCommand { get; }

        public ProfileManager ProfileManager => _profileManager;

        // Changes the power mode of the power source in use (tray menu).
        public void SetProfile(CoolingProfileType type)
        {
            SetProfile(type, _powerManager.CurrentPowerSource);
        }

        // Changes the power mode of one power source only. The other source's mode and any fan
        // speed the user has set are left as they are.
        public void SetProfile(CoolingProfileType type, PowerSource source)
        {
            bool ok = _profileManager.SetProfile(type, source);
            if (ok)
            {
                var applied = _profileManager.GetActiveProfile(source);
                string? failure = DescribeLastFailure();
                StatusMessage = failure != null
                    ? $"{applied.Name} only partly applied: {failure}."
                    : $"{DescribeSource(source)} Power Mode Set to {applied.Name} (fan speed unchanged)";
                IsStockRestored = false;
            }
            else
            {
                StatusMessage = "Failed to switch power mode.";
            }
        }

        // Changes the fan speed only. The power mode is left as it is.
        public void ApplyManualRpm()
        {
            if (!_powerManager.IsDirectSamAvailable)
            {
                StatusMessage = "Administrator privileges required for direct SAM RPM override.";
                return;
            }

            ushort target = (ushort)Math.Clamp(_manualSliderRpm, 0, SurfaceSamClient.MaxManualFanRpm);
            bool ok = _profileManager.SetManualRpm(target);
            if (ok)
            {
                ushort effectiveRpm = _powerManager.ActiveTargetRpm;
                ManualSliderRpm = effectiveRpm;
                TargetFanRpm = effectiveRpm;
                StatusMessage = effectiveRpm > 0
                    ? $"Fan Speed Set to {effectiveRpm} RPM (power mode unchanged)"
                    : "Fan Returned to Automatic Control (power mode unchanged)";
                IsStockRestored = false;
            }
            else
            {
                StatusMessage = "Failed to apply fan speed.";
            }
        }

        public void ResetFanOnly()
        {
            bool ok = _profileManager.ResetFanSpeedOnly();
            if (ok)
            {
                ManualSliderRpm = 0;
                TargetFanRpm = 0;
                StatusMessage = _powerManager.IsDirectSamAvailable
                    ? "Fan speed reset to automatic SAM control (0 RPM override; power limits unchanged)"
                    : "Fan target reset (Admin required for direct SAM command; power limits unchanged)";
            }
            else
            {
                StatusMessage = "Failed to reset fan speed.";
            }
        }

        public void OpenExternalLink(string uri)
        {
            try
            {
                // Through the desktop shell, so the browser or mail app does not inherit this
                // app's administrator rights.
                Services.ShellLauncher.OpenUnelevated(uri);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open link: {ex.Message}";
            }
        }

        public void RestoreStock()
        {
            bool ok = _profileManager.RestoreStock();
            if (ok)
            {
                TargetFanRpm = _powerManager.ActiveTargetRpm;
                if (_manualSliderRpm == 0)
                {
                    ManualSliderRpm = 3400;
                }
                string? failure = DescribeLastFailure();
                StatusMessage = failure != null
                    ? $"Stock restore only partly applied: {failure}."
                    : "OEM Stock Settings & Automatic SAM Fan Curve Restored";
                IsStockRestored = true;
            }
            else
            {
                StatusMessage = "Failed to restore stock settings.";
            }
        }

        // Says what the last profile apply or stock restore could not do, or null when every
        // step succeeded, so the status line never reports a clean success after a failure.
        private string? DescribeLastFailure()
        {
            bool overlayFailed = _powerManager.LastOverlayFailed;
            bool fanFailed = _powerManager.LastFanCommandFailed;

            if (overlayFailed && fanFailed)
                return "the Windows power mode and the SAM fan command both failed";
            if (overlayFailed)
                return "the Windows power mode could not be changed";
            if (fanFailed)
                return "the SAM fan command failed";
            return null;
        }

        // The SAM accepts an RPM command even when its own thermal control then ignores it, so the
        // measured speed is what shows whether a target took. Say so once when it did not, rather
        // than leaving the earlier "set to N RPM" message standing.
        private void UpdateFanTargetState(FanTargetState state)
        {
            if (state == _fanTargetState) return;

            _fanTargetState = state;
            OnPropertyChanged(nameof(FanStateSubtitle));

            if (state == FanTargetState.NotApplied)
            {
                StatusMessage = $"Firmware is not applying the {_targetFanRpm} RPM target; the fan is at {_currentFanRpm} RPM under its own control.";
            }
            else if (state == FanTargetState.HeldAboveTarget)
            {
                StatusMessage = $"Firmware is holding the fan at {_currentFanRpm} RPM, above the {_targetFanRpm} RPM target.";
            }
        }

        private void OnSnapshotUpdated(SystemThermalSnapshot snapshot)
        {
            if (_disposed) return;

            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (_disposed) return;

                CurrentPeakTemp = snapshot.PeakTemperatureCelsius;
                CurrentAvgTemp = snapshot.AverageTemperatureCelsius;
                CpuFrequency = snapshot.CpuFrequencyMhz;
                CpuPerformance = snapshot.CpuPerformancePercent;
                CurrentFanRpm = snapshot.FanRpm;
                TargetFanRpm = snapshot.TargetFanRpm;
                UpdateFanTargetState(snapshot.FanTargetState);
                SmfFanProfile = snapshot.SmfFanProfile;
                Pl1Watts = snapshot.Pl1Watts;
                Pl2Watts = snapshot.Pl2Watts;
                IsDirectSamAvailable = snapshot.IsDirectSamAvailable;
                History.Record(snapshot);

                UpdateSourceInUse(snapshot.IsPluggedIn ? PowerSource.PluggedIn : PowerSource.Battery);

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

        private void OnProfileChanged(CoolingProfile profile, PowerSource source)
        {
            void Apply()
            {
                if (_disposed) return;
                if (source == _powerManager.CurrentPowerSource)
                {
                    ActiveProfile = profile;
                }
                RaiseModeFlagsChanged();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(Apply);
            }
            else
            {
                Apply();
            }
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
            Battery.Dispose();
            Display.Dispose();
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
