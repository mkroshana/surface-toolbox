using SurfaceToolbox.UI.ViewModels;

namespace SurfaceToolbox.UI.Views
{
    public partial class DisplayPage : System.Windows.Controls.UserControl
    {
        public DisplayPage()
        {
            InitializeComponent();

            // Poll the display only while this page is the one on screen.
            Loaded += (_, _) => (DataContext as DisplayViewModel)?.Start();
            Unloaded += (_, _) => (DataContext as DisplayViewModel)?.Stop();
        }
    }
}
