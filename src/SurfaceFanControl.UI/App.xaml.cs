using System;
using System.Threading;
using System.Windows;
using SurfaceFanControl.UI.Tray;
using SurfaceFanControl.UI.ViewModels;
using SurfaceFanControl.UI.Views;
using Application = System.Windows.Application;

namespace SurfaceFanControl.UI
{
    public partial class App : Application
    {
        private const string MutexName = "Local\\SurfaceFanControl_SingleInstanceMutex";
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
                    "Surface Fan Control is already running in the system tray.",
                    "Surface Fan Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _viewModel = new MainViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel
            };

            _trayManager = new TrayIconManager(_viewModel, _mainWindow);

            // Show main window on first launch
            _mainWindow.Show();
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
