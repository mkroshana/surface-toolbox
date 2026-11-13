using SurfaceToolbox.UI.ViewModels;

namespace SurfaceToolbox.UI.Views
{
    public partial class HistoryPage : System.Windows.Controls.UserControl
    {
        public HistoryPage()
        {
            InitializeComponent();

            // Readings are recorded all the time; the charts are only redrawn while this page is shown.
            Loaded += (_, _) => (DataContext as HistoryViewModel)?.SetVisible(true);
            Unloaded += (_, _) => (DataContext as HistoryViewModel)?.SetVisible(false);
        }
    }
}
