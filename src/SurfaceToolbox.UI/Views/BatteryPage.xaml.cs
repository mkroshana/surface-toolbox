using SurfaceToolbox.UI.ViewModels;

namespace SurfaceToolbox.UI.Views
{
    public partial class BatteryPage : System.Windows.Controls.UserControl
    {
        public BatteryPage()
        {
            InitializeComponent();

            // The page is loaded when its sidebar entry is selected and unloaded when another
            // page is shown, so the battery is only polled while someone is looking at it.
            Loaded += (_, _) => (DataContext as BatteryViewModel)?.Start();
            Unloaded += (_, _) => (DataContext as BatteryViewModel)?.Stop();
        }
    }
}
