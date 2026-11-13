using SurfaceFanControl.UI.ViewModels;

namespace SurfaceFanControl.UI.Views
{
    public partial class SettingsPage : System.Windows.Controls.UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();

            // Task Scheduler can be changed outside the app, so re-read the state each time the page is opened.
            Loaded += (_, _) => (DataContext as SettingsViewModel)?.Refresh();
        }
    }
}
