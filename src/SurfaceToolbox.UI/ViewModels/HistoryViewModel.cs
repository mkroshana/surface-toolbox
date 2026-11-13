using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using SurfaceToolbox.Core.Models;

namespace SurfaceToolbox.UI.ViewModels
{
    /// <summary>
    /// Backs the History page. Readings are recorded the whole time the app runs (up to an hour,
    /// in memory only); the charts are rebuilt only while the page is on screen.
    /// </summary>
    public class HistoryViewModel : INotifyPropertyChanged
    {
        // Enough points for a smooth line at the page's width without drawing one per reading.
        private const int MaxChartPoints = 400;

        private readonly TelemetryHistory _history = new();
        private int _windowMinutes = 5;
        private bool _isVisible;
        private IReadOnlyList<double> _temperatureValues = Array.Empty<double>();
        private IReadOnlyList<double> _fanValues = Array.Empty<double>();
        private IReadOnlyList<double> _cpuValues = Array.Empty<double>();
        private string _temperatureStats = string.Empty;
        private string _fanStats = string.Empty;
        private string _cpuStats = string.Empty;
        private string _coverageText = "No readings yet";
        private string _exportStatus = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HistoryViewModel()
        {
            ExportCsvCommand = new RelayCommand(_ => ExportWithDialog());
        }

        public ICommand ExportCsvCommand { get; }

        public IReadOnlyList<double> TemperatureValues { get => _temperatureValues; private set { _temperatureValues = value; Raise(nameof(TemperatureValues)); } }
        public IReadOnlyList<double> FanValues { get => _fanValues; private set { _fanValues = value; Raise(nameof(FanValues)); } }
        public IReadOnlyList<double> CpuValues { get => _cpuValues; private set { _cpuValues = value; Raise(nameof(CpuValues)); } }

        public string TemperatureStats { get => _temperatureStats; private set { _temperatureStats = value; Raise(nameof(TemperatureStats)); } }
        public string FanStats { get => _fanStats; private set { _fanStats = value; Raise(nameof(FanStats)); } }
        public string CpuStats { get => _cpuStats; private set { _cpuStats = value; Raise(nameof(CpuStats)); } }

        /// <summary>How much of the chosen window actually has readings behind it.</summary>
        public string CoverageText { get => _coverageText; private set { _coverageText = value; Raise(nameof(CoverageText)); } }

        public string ExportStatus { get => _exportStatus; private set { _exportStatus = value; Raise(nameof(ExportStatus)); } }

        public bool IsWindow5 { get => _windowMinutes == 5; set { if (value) SetWindow(5); } }
        public bool IsWindow15 { get => _windowMinutes == 15; set { if (value) SetWindow(15); } }
        public bool IsWindow60 { get => _windowMinutes == 60; set { if (value) SetWindow(60); } }

        private void SetWindow(int minutes)
        {
            if (_windowMinutes == minutes) return;
            _windowMinutes = minutes;
            Raise(nameof(IsWindow5));
            Raise(nameof(IsWindow15));
            Raise(nameof(IsWindow60));
            Refresh();
        }

        /// <summary>Records one reading. Called on the UI thread for every monitor snapshot.</summary>
        public void Record(SystemThermalSnapshot snapshot)
        {
            _history.Add(snapshot);
            if (_isVisible) Refresh();
        }

        /// <summary>Called when the page is shown or hidden; the charts are only rebuilt while it is shown.</summary>
        public void SetVisible(bool isVisible)
        {
            _isVisible = isVisible;
            if (isVisible) Refresh();
        }

        private void Refresh()
        {
            TelemetrySample[] samples = _history.Snapshot(TimeSpan.FromMinutes(_windowMinutes));
            if (samples.Length == 0)
            {
                TemperatureValues = Array.Empty<double>();
                FanValues = Array.Empty<double>();
                CpuValues = Array.Empty<double>();
                TemperatureStats = FanStats = CpuStats = string.Empty;
                CoverageText = "No readings yet";
                return;
            }

            double[] temperature = samples.Select(s => s.PeakTempC).ToArray();
            double[] fan = samples.Select(s => (double)s.FanRpm).ToArray();
            double[] cpu = samples.Select(s => s.CpuLoadPercent).ToArray();

            TemperatureValues = TelemetryHistory.Downsample(temperature, MaxChartPoints);
            FanValues = TelemetryHistory.Downsample(fan, MaxChartPoints);
            CpuValues = TelemetryHistory.Downsample(cpu, MaxChartPoints);

            TemperatureStats = $"Now {temperature[^1]:F1} °C   Low {temperature.Min():F1}   High {temperature.Max():F1}";
            FanStats = $"Now {fan[^1]:F0} RPM   Low {fan.Min():F0}   High {fan.Max():F0}";
            CpuStats = $"Now {cpu[^1]:F0}%   Low {cpu.Min():F0}   High {cpu.Max():F0}";

            TimeSpan covered = samples[^1].TimestampUtc - samples[0].TimestampUtc;
            CoverageText = $"{FormatSpan(covered)} recorded of the last {_windowMinutes} min, {samples.Length} readings";
        }

        private static string FormatSpan(TimeSpan span)
        {
            return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds:00} s" : $"{span.Seconds} s";
        }

        private void ExportWithDialog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Readings",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = $"surface-readings-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportCsv(dialog.FileName);
            }
        }

        /// <summary>Writes every recorded reading (the full hour, not just the chosen window) to a CSV file.</summary>
        public bool ExportCsv(string path)
        {
            try
            {
                using var writer = new StreamWriter(path, false);
                int count = _history.WriteCsv(writer);
                ExportStatus = $"Exported {count} readings to {path}";
                return true;
            }
            catch (Exception ex)
            {
                ExportStatus = $"Could not export: {ex.Message}";
                return false;
            }
        }

        private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
