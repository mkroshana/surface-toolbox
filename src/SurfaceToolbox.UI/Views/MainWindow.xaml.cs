using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SurfaceToolbox.UI.ViewModels;

namespace SurfaceToolbox.UI.Views
{
    public partial class MainWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private bool _isExplicitExit;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int darkMode = 1;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                    }
                }
            }
            catch
            {
                // Fallback gracefully on older OS versions
            }
        }

        public void PrepareForExplicitExit()
        {
            _isExplicitExit = true;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Minimize to tray instead of quitting
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
