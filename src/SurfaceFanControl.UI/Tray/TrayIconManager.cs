using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using SurfaceFanControl.Core.Profiles;
using SurfaceFanControl.UI.ViewModels;
using Application = System.Windows.Application;

namespace SurfaceFanControl.UI.Tray
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly MainViewModel _viewModel;
        private readonly Window _mainWindow;
        private Icon? _generatedIcon;

        public TrayIconManager(MainViewModel viewModel, Window mainWindow)
        {
            _viewModel = viewModel;
            _mainWindow = mainWindow;

            _generatedIcon = CreateMinimalistFanIcon();

            _notifyIcon = new NotifyIcon
            {
                Icon = _generatedIcon,
                Text = "Surface Fan Control",
                Visible = true
            };

            BuildContextMenu();

            _notifyIcon.DoubleClick += (s, e) => ToggleMainWindow();

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentPeakTemp) ||
                    e.PropertyName == nameof(MainViewModel.CurrentFanRpm) ||
                    e.PropertyName == nameof(MainViewModel.ActiveProfile) ||
                    e.PropertyName == nameof(MainViewModel.PowerSourceText))
                {
                    UpdateTooltip();
                }
            };

            UpdateTooltip();
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var header = new ToolStripMenuItem("Surface Pro 9 Fan Control") { Enabled = false };
            header.Font = new Font(header.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            var quietItem = new ToolStripMenuItem("Quiet / Silent", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.Quiet));
            var balancedItem = new ToolStripMenuItem("Balanced (Auto)", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.Balanced));
            var turboItem = new ToolStripMenuItem("Turbo Cooling (4200 RPM)", null, (s, e) => _viewModel.SetProfile(CoolingProfileType.Turbo));

            menu.Items.Add(quietItem);
            menu.Items.Add(balancedItem);
            menu.Items.Add(turboItem);

            menu.Items.Add(new ToolStripSeparator());

            var stockItem = new ToolStripMenuItem("Reset to Stock Defaults", null, (s, e) => _viewModel.RestoreStock());
            menu.Items.Add(stockItem);

            menu.Items.Add(new ToolStripSeparator());

            var showItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowMainWindow());
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);

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
            Application.Current.Shutdown();
        }

        private static Icon CreateMinimalistFanIcon()
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var pen = new Pen(Color.FromArgb(0, 120, 215), 2.5f);
                g.DrawEllipse(pen, 3, 3, 26, 26);

                using var hubBrush = new SolidBrush(Color.White);
                g.FillEllipse(hubBrush, 13, 13, 6, 6);

                using var bladeBrush = new SolidBrush(Color.FromArgb(220, 240, 255));
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * 120f;
                    var state = g.Save();
                    g.TranslateTransform(16, 16);
                    g.RotateTransform(angle);
                    g.FillPolygon(bladeBrush, new PointF[]
                    {
                        new PointF(0, -3),
                        new PointF(4, -10),
                        new PointF(2, -12),
                        new PointF(-2, -7)
                    });
                    g.Restore(state);
                }
            }

            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _generatedIcon?.Dispose();
        }
    }
}
