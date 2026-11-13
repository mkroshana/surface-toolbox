using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using SurfaceFanControl.Core.Profiles;
using SurfaceFanControl.UI.ViewModels;
using Application = System.Windows.Application;

namespace SurfaceFanControl.UI.Tray
{
    public class TrayIconManager : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon _notifyIcon;
        private readonly MainViewModel _viewModel;
        private readonly Window _mainWindow;
        private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
        private ContextMenuStrip? _contextMenu;
        private Font? _headerFont;
        private Icon? _generatedIcon;

        public TrayIconManager(MainViewModel viewModel, Window mainWindow)
        {
            _viewModel = viewModel;
            _mainWindow = mainWindow;

            _generatedIcon = LoadTrayIcon();

            _notifyIcon = new NotifyIcon
            {
                Icon = _generatedIcon,
                Text = "Surface Fan Control",
                Visible = true
            };

            BuildContextMenu();

            _notifyIcon.DoubleClick += (s, e) => ToggleMainWindow();

            _viewModelPropertyChangedHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentPeakTemp) ||
                    e.PropertyName == nameof(MainViewModel.CurrentFanRpm) ||
                    e.PropertyName == nameof(MainViewModel.ActiveProfile))
                {
                    UpdateTooltip();
                }
            };
            _viewModel.PropertyChanged += _viewModelPropertyChangedHandler;

            UpdateTooltip();
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var header = new ToolStripMenuItem($"Surface Fan Control — {_viewModel.DeviceName}") { Enabled = false };
            _headerFont = new Font(header.Font, System.Drawing.FontStyle.Bold);
            header.Font = _headerFont;
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // Power modes only; none of these changes a fan speed the user has set.
            var quietItem = new ToolStripMenuItem("Power Mode: Quiet", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.Quiet));
            var balancedItem = new ToolStripMenuItem("Power Mode: Balanced", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.Balanced));
            var betterItem = new ToolStripMenuItem("Power Mode: Better Performance", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.BetterPerformance));
            var bestItem = new ToolStripMenuItem("Power Mode: Best Performance", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.BestPerformance));

            menu.Items.Add(quietItem);
            menu.Items.Add(balancedItem);
            menu.Items.Add(betterItem);
            menu.Items.Add(bestItem);

            menu.Items.Add(new ToolStripSeparator());

            var resetFanItem = new ToolStripMenuItem("Reset Fan Speed (Auto)", null, (s, e) => _viewModel.ResetFanOnly());
            var stockItem = new ToolStripMenuItem("Reset to Stock Defaults", null, (s, e) => _viewModel.RestoreStock());
            menu.Items.Add(resetFanItem);
            menu.Items.Add(stockItem);

            menu.Items.Add(new ToolStripSeparator());

            var showItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowMainWindow());
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);

            _contextMenu = menu;
            _notifyIcon.ContextMenuStrip = menu;
        }

        public void UpdateTooltip()
        {
            try
            {
                string text = $"Surface: {_viewModel.ActiveProfile.Name}\nFan: {_viewModel.CurrentFanRpm} RPM | Peak: {_viewModel.CurrentPeakTemp:F1}°C";
                if (text.Length > 63)
                {
                    text = $"Surface: {_viewModel.CurrentFanRpm} RPM ({_viewModel.CurrentPeakTemp:F0}°C)";
                }
                _notifyIcon.Text = text;
            }
            catch { }
        }

        public void ToggleMainWindow()
        {
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Hide();
            }
            else
            {
                ShowMainWindow();
            }
        }

        public void ShowMainWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            if (_mainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.PrepareForExplicitExit();
            }
            _mainWindow.Close();
            Application.Current.Shutdown();
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch { }

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(icoPath))
                {
                    return new Icon(icoPath, 32, 32);
                }

                var procPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(procPath);
                    if (extracted != null) return extracted;
                }
            }
            catch { }

            return CreateMinimalistFanIcon();
        }

        private static Icon CreateMinimalistFanIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var pen = new Pen(Color.FromArgb(0, 120, 215), 2.2f);
                g.DrawEllipse(pen, 2, 2, 28, 28);

                using var hubBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
                g.FillEllipse(hubBrush, 12, 12, 8, 8);

                using var bladeBrush = new SolidBrush(Color.FromArgb(96, 165, 250));
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * 90f;
                    var state = g.Save();
                    g.TranslateTransform(16, 16);
                    g.RotateTransform(angle);
                    g.FillPolygon(bladeBrush, new PointF[]
                    {
                        new PointF(0, -3),
                        new PointF(5, -11),
                        new PointF(2, -13),
                        new PointF(-2, -6)
                    });
                    g.Restore(state);
                }

                using var hubRingPen = new Pen(Color.FromArgb(56, 189, 248), 1.2f);
                g.DrawEllipse(hubRingPen, 12, 12, 8, 8);

                using var dotBrush = new SolidBrush(Color.White);
                g.FillEllipse(dotBrush, 14, 14, 4, 4);
            }

            IntPtr hIcon = bitmap.GetHicon();
            Icon managed = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            return managed;
        }

        public void Dispose()
        {
            _viewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu?.Dispose();
            _headerFont?.Dispose();
            _generatedIcon?.Dispose();
        }
    }
}
