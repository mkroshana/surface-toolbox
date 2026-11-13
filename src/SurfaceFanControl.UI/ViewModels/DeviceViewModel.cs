using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SurfaceFanControl.Core.Hardware;
using SurfaceFanControl.Core.Models;

namespace SurfaceFanControl.UI.ViewModels
{
    /// <summary>One labelled value on an information page.</summary>
    public class InfoRow
    {
        public InfoRow(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public string Value { get; }
    }

    /// <summary>
    /// Backs the Device page: what Windows reports about the device, its firmware versions and
    /// its storage. Read-only. Loaded the first time the page is opened and again on Refresh.
    /// </summary>
    public class DeviceViewModel : INotifyPropertyChanged
    {
        private const string NotReported = "Not reported";

        private readonly SurfaceDeviceReportReader _reader = new();
        private IReadOnlyList<InfoRow> _deviceRows = Array.Empty<InfoRow>();
        private IReadOnlyList<InfoRow> _firmwareRows = Array.Empty<InfoRow>();
        private IReadOnlyList<InfoRow> _storageRows = Array.Empty<InfoRow>();
        private string _statusText = string.Empty;
        private bool _isLoading;
        private bool _hasLoaded;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DeviceViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Load(), _ => !_isLoading);
        }

        public ICommand RefreshCommand { get; }

        public IReadOnlyList<InfoRow> DeviceRows { get => _deviceRows; private set { _deviceRows = value; Raise(nameof(DeviceRows)); } }
        public IReadOnlyList<InfoRow> FirmwareRows { get => _firmwareRows; private set { _firmwareRows = value; Raise(nameof(FirmwareRows)); } }
        public IReadOnlyList<InfoRow> StorageRows { get => _storageRows; private set { _storageRows = value; Raise(nameof(StorageRows)); } }
        public string StatusText { get => _statusText; private set { _statusText = value; Raise(nameof(StatusText)); } }

        /// <summary>Loads the report the first time the page is shown; later visits keep what is there.</summary>
        public void EnsureLoaded()
        {
            if (!_hasLoaded) Load();
        }

        private void Load()
        {
            if (_isLoading) return;
            _isLoading = true;
            _hasLoaded = true;
            StatusText = "Reading device information...";

            Task.Run(() =>
            {
                DeviceReport? report = null;
                string? error = null;
                try { report = _reader.Read(); }
                catch (Exception ex) { error = ex.Message; }

                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    _isLoading = false;
                    if (report != null)
                    {
                        Apply(report);
                        StatusText = $"Read at {DateTime.Now:HH:mm:ss}. Serial numbers are not shown.";
                    }
                    else
                    {
                        StatusText = $"Could not read device information: {error}";
                    }
                    CommandManager.InvalidateRequerySuggested();
                });
            });
        }

        // Public so the mapping from report to rows can be checked without a window.
        public void Apply(DeviceReport report)
        {
            DeviceRows = new List<InfoRow>
            {
                new("Model", Or(report.Model)),
                new("SKU", Or(report.Sku)),
                new("Processor", Or(report.Processor)),
                new("Memory", report.MemoryGb is double memory ? $"{memory:F1} GB" : NotReported),
                new("Windows", string.IsNullOrEmpty(report.WindowsName) ? NotReported : $"{report.WindowsName} (build {report.WindowsBuild})"),
                new("UEFI", string.IsNullOrEmpty(report.UefiVersion)
                    ? NotReported
                    : report.UefiReleaseDate is DateTime released ? $"{report.UefiVersion} ({released:d MMM yyyy})" : report.UefiVersion)
            };

            var firmware = new List<InfoRow>();
            foreach (FirmwareComponent component in report.Firmware)
                firmware.Add(new InfoRow(component.Name, component.Version));
            if (firmware.Count == 0)
                firmware.Add(new InfoRow("Firmware Components", NotReported));
            FirmwareRows = firmware;

            var storage = new List<InfoRow>();
            foreach (StorageDrive drive in report.Drives)
            {
                storage.Add(new InfoRow("Drive", Or(drive.Model)));
                storage.Add(new InfoRow("Type", drive.SizeGb is double size ? $"{drive.Kind}, {size:F0} GB" : drive.Kind));
                storage.Add(new InfoRow("Health", Or(drive.Health)));
                storage.Add(new InfoRow("Life Used", drive.WearPercent is int wear ? $"{wear}%" : NotReported));
                storage.Add(new InfoRow("Temperature", drive.TemperatureCelsius is int temperature ? $"{temperature} °C" : NotReported));
                storage.Add(new InfoRow("Drive Firmware", Or(drive.FirmwareVersion)));
            }
            foreach (StorageVolume volume in report.Volumes)
                storage.Add(new InfoRow($"Volume {volume.Letter}", $"{volume.FreeGb:F0} GB free of {volume.SizeGb:F0} GB"));
            if (storage.Count == 0)
                storage.Add(new InfoRow("Storage", NotReported));
            StorageRows = storage;
        }

        private static string Or(string value) => string.IsNullOrWhiteSpace(value) ? NotReported : value;

        private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
