using System.ComponentModel;
using System.Windows;
using SurfaceFanControl.UI.ViewModels;

namespace SurfaceFanControl.UI.Views
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit;

        public MainWindow()
        {
            InitializeComponent();
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
