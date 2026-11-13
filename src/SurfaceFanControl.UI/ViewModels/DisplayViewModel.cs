using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SurfaceFanControl.Core.Hardware;
using SurfaceFanControl.Core.Models;

namespace SurfaceFanControl.UI.ViewModels
{
    /// <summary>One refresh rate the display offers; shown as a segment the user can pick.</summary>
    public class RefreshRateOption : INotifyPropertyChanged
    {
        private readonly Action<int> _select;
        private bool _isSelected;

        public RefreshRateOption(int hz, Action<int> select)
        {
            Hz = hz;
            _select = select;
        }

        public int Hz { get; }
        public string Label => $"{Hz} Hz";

        /// <summary>True for the rate in use. Setting it to true asks for that rate.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value && !_isSelected)
                    _select(Hz);
            }
        }

        // Called by the page's view model once it knows which rate Windows is really using.
        public void Reflect(bool isSelected)
        {
            _isSelected = isSelected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Backs the Display page: refresh rate and brightness of the built-in display. It polls
    /// only while the page is open, so changes made elsewhere in Windows show up here too.
    /// </summary>
    public class DisplayViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string NotReported = "Not reported";
        private const int PollIntervalMs = 3000;

        private readonly SurfaceDisplayClient _client = new();
        private readonly object _timerLock = new();
        private readonly DispatcherTimer _brightnessDebounce;
        private System.Threading.Timer? _timer;
        private DisplayState? _state;
        private int _brightness;
        private int _pendingBrightness = -1;
        private string _statusText = string.Empty;
        private int _isReading;
        private volatile bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DisplayViewModel()
        {
            // Dragging the slider raises many values; apply the one it settles on.
            _brightnessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _brightnessDebounce.Tick += (_, _) =>
            {
                _brightnessDebounce.Stop();
                ApplyBrightness();
            };
        }

        public ObservableCollection<RefreshRateOption> RefreshRates { get; } = new();

        public string ResolutionText => _state != null ? $"{_state.Width} × {_state.Height}" : NotReported;
        public string RefreshRateText => _state != null ? $"{_state.RefreshRateHz} Hz" : NotReported;
        public string BrightnessText => _state?.BrightnessPercent != null ? $"{_brightness}%" : NotReported;
        public bool IsBrightnessAvailable => _state?.BrightnessPercent != null;

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; Raise(nameof(StatusText)); }
        }

        /// <summary>Brightness slider, 0 to 100. Setting it changes the panel after a short pause.</summary>
        public int Brightness
        {
            get => _brightness;
            set
            {
                int clamped = Math.Clamp(value, 0, 100);
                if (clamped == _brightness) return;
                _brightness = clamped;
                _pendingBrightness = clamped;
                Raise(nameof(Brightness));
                Raise(nameof(BrightnessText));
                _brightnessDebounce.Stop();
                _brightnessDebounce.Start();
            }
        }

        public void Start()
        {
            lock (_timerLock)
            {
                if (_disposed || _timer != null) return;
                _timer = new System.Threading.Timer(_ => Refresh(), null, 0, PollIntervalMs);
            }
        }

        public void Stop()
        {
            lock (_timerLock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private void Refresh()
        {
            if (_disposed || Interlocked.CompareExchange(ref _isReading, 1, 0) != 0)
                return;

            try
            {
                DisplayState? state = _client.Read();
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => Apply(state), DispatcherPriority.Background);
            }
            catch
            {
                // Keep the last values on screen; the next tick tries again.
            }
            finally
            {
                Interlocked.Exchange(ref _isReading, 0);
            }
        }

        private void Apply(DisplayState? state)
        {
            if (_disposed) return;
            _state = state;

            if (state != null)
            {
                // Rebuild the choices only when the set of rates changes (for example another display became primary).
                if (!RefreshRates.Select(r => r.Hz).SequenceEqual(state.AvailableRefreshRatesHz))
                {
                    RefreshRates.Clear();
                    foreach (int hz in state.AvailableRefreshRatesHz)
                        RefreshRates.Add(new RefreshRateOption(hz, RequestRefreshRate));
                }
                foreach (RefreshRateOption option in RefreshRates)
                    option.Reflect(option.Hz == state.RefreshRateHz);

                // Do not fight the slider while the user's own change is still on its way to the panel.
                if (state.BrightnessPercent is int brightness && _pendingBrightness < 0)
                    _brightness = brightness;
            }

            Raise(nameof(ResolutionText));
            Raise(nameof(RefreshRateText));
            Raise(nameof(Brightness));
            Raise(nameof(BrightnessText));
            Raise(nameof(IsBrightnessAvailable));
        }

        private void RequestRefreshRate(int hz)
        {
            Task.Run(() =>
            {
                bool ok = _client.SetRefreshRate(hz);
                DisplayState? state = _client.Read();
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    Apply(state);
                    StatusText = ok ? $"Refresh Rate Set to {hz} Hz" : $"Windows refused {hz} Hz; the refresh rate is unchanged.";
                });
            });
        }

        private void ApplyBrightness()
        {
            int target = _pendingBrightness;
            if (target < 0) return;

            Task.Run(() =>
            {
                bool ok = _client.SetBrightness(target);
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    if (_pendingBrightness == target) _pendingBrightness = -1;
                    StatusText = ok ? $"Brightness Set to {target}%" : "Could not change the brightness.";
                });
            });
        }

        private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            _disposed = true;
            _brightnessDebounce.Stop();
            Stop();
        }
    }
}
