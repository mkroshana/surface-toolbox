using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using SurfaceFanControl.Core.Hardware;
using SurfaceFanControl.UI.Services;

namespace SurfaceFanControl.UI.ViewModels
{
    /// <summary>
    /// Backs the Diagnostics page: has Windows produce its own battery report and sleep study and
    /// opens them, and links to the Windows power settings. Nothing here changes a setting.
    /// </summary>
    public class DiagnosticsViewModel : INotifyPropertyChanged
    {
        private readonly PowerReportGenerator _generator = new();
        private readonly Action<string> _open;
        private readonly Action<Action> _post;
        private string _batteryStatus = "Not created yet in this session.";
        private string _sleepStatus = "Not created yet in this session.";
        private bool _isBusy;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <param name="open">How to open a file or link; defaults to the unelevated shell launcher.</param>
        /// <param name="post">How to get back onto the UI thread; defaults to the app's dispatcher.</param>
        public DiagnosticsViewModel(Action<string>? open = null, Action<Action>? post = null)
        {
            _open = open ?? ShellLauncher.OpenUnelevated;
            _post = post ?? (action => System.Windows.Application.Current?.Dispatcher?.InvokeAsync(action));

            ReportsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SurfaceFanControl", "Reports");
            CreateBatteryReportCommand = new RelayCommand(parameter => { _ = CreateAsync(PowerReportKind.Battery); }, parameter => !_isBusy);
            CreateSleepStudyCommand = new RelayCommand(parameter => { _ = CreateAsync(PowerReportKind.SleepStudy); }, parameter => !_isBusy);
            OpenPowerSettingsCommand = new RelayCommand(_ => OpenPowerSettings());
        }

        public string ReportsFolder { get; }

        public ICommand CreateBatteryReportCommand { get; }
        public ICommand CreateSleepStudyCommand { get; }
        public ICommand OpenPowerSettingsCommand { get; }

        public string BatteryStatus { get => _batteryStatus; private set { _batteryStatus = value; Raise(nameof(BatteryStatus)); } }
        public string SleepStatus { get => _sleepStatus; private set { _sleepStatus = value; Raise(nameof(SleepStatus)); } }

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; Raise(nameof(IsBusy)); }
        }

        /// <summary>Creates one report and opens it. Returns once the report has been created or has failed.</summary>
        public async Task CreateAsync(PowerReportKind kind)
        {
            if (_isBusy) return;
            IsBusy = true;
            SetStatus(kind, "Windows is writing the report...");

            string path = Path.Combine(ReportsFolder, kind == PowerReportKind.Battery ? "battery-report.html" : "sleep-study.html");
            PowerReportResult result = await Task.Run(() => _generator.Generate(kind, path)).ConfigureAwait(false);

            _post(() =>
            {
                IsBusy = false;
                if (result.Succeeded)
                {
                    SetStatus(kind, $"Created {DateTime.Now:HH:mm:ss} and opened: {result.Path}");
                    try { _open(result.Path); }
                    catch (Exception ex) { SetStatus(kind, $"Created at {result.Path}, but it could not be opened: {ex.Message}"); }
                }
                else
                {
                    SetStatus(kind, $"Windows could not create the report: {FirstLine(result.Output)}");
                }
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OpenPowerSettings()
        {
            try { _open("ms-settings:powersleep"); }
            catch (Exception ex) { BatteryStatus = $"Could not open Windows settings: {ex.Message}"; }
        }

        private void SetStatus(PowerReportKind kind, string text)
        {
            if (kind == PowerReportKind.Battery) BatteryStatus = text; else SleepStatus = text;
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            string line = end >= 0 ? text.Substring(0, end) : text;
            return line.Length > 0 ? line : "no reason given";
        }

        private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
