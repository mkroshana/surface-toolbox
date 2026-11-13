using SurfaceFanControl.UI.ViewModels;

namespace SurfaceFanControl.UI.Views
{
    public partial class DevicePage : System.Windows.Controls.UserControl
    {
        public DevicePage()
        {
            InitializeComponent();

            // The report is read the first time the page is opened, not at app start.
            Loaded += (_, _) => (DataContext as DeviceViewModel)?.EnsureLoaded();
        }
    }
}
