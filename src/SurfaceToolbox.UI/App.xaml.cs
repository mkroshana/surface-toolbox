using System;
using System.Threading;
using System.Windows;
using SurfaceToolbox.Core.Hardware;
using SurfaceToolbox.UI.Tray;
using SurfaceToolbox.UI.ViewModels;
using SurfaceToolbox.UI.Views;
using Application = System.Windows.Application;

namespace SurfaceToolbox.UI
{
    public partial class App : Application
    {
        private const string MutexName = "Local\\SurfaceToolbox_SingleInstanceMutex";
        private Mutex? _singleInstanceMutex;
        private bool _ownsMutex;
        private TrayIconManager? _trayManager;
        private MainWindow? _mainWindow;
        private MainViewModel? _viewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool isInitialInstance;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out isInitialInstance);
            }
            catch (AbandonedMutexException)
            {
                isInitialInstance = true;
            }
            catch (UnauthorizedAccessException)
            {
                isInitialInstance = false;
            }

            _ownsMutex = isInitialInstance;
            if (!isInitialInstance)
            {
                System.Windows.MessageBox.Show(
                    "Surface Toolbox is already running in the system tray.",
                    "Surface Toolbox",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Refuse to run on anything that is not a Surface: the profiles write power
            // settings and send SAM fan commands that are only meaningful on Surface hardware.
            var deviceInfo = SurfaceDeviceDetector.Detect();
            if (!deviceInfo.IsSurfaceDevice)
            {
                string detected = $"{deviceInfo.Manufacturer} {deviceInfo.Model}".Trim();
                if (detected.Length == 0)
                {
                    detected = "Unknown";
                }

                System.Windows.MessageBox.Show(
                    "Surface Toolbox only runs on Microsoft Surface devices.\n\n" +
                    $"Detected device: {detected}",
                    "Surface Toolbox",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            _viewModel = new MainViewModel(deviceInfo);
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel
            };

            _trayManager = new TrayIconManager(_viewModel, _mainWindow);

            // Carry a sign-in task registered under the app's old name over to the new one. Off the
            // UI thread, since it asks Task Scheduler and nothing on screen waits for the answer.
            string? executablePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executablePath))
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { new Services.AutostartService().MigrateLegacyTask(executablePath); }
                    catch { /* Left as it was; the Settings page still shows the real state. */ }
                });
            }

            // Started by the sign-in task: stay in the tray. Otherwise open the dashboard.
            bool startInTray = Array.Exists(e.Args, arg => string.Equals(arg, Services.AutostartService.TrayArgument, StringComparison.OrdinalIgnoreCase));
            if (!startInTray)
            {
                _mainWindow.Show();
            }
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            _mainWindow?.PrepareForExplicitExit();
            base.OnSessionEnding(e);
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainWindow?.PrepareForExplicitExit();
            _trayManager?.Dispose();
            _viewModel?.Dispose();

            if (_singleInstanceMutex != null)
            {
                if (_ownsMutex)
                {
                    try
                    {
                        _singleInstanceMutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                    }
                    _ownsMutex = false;
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
