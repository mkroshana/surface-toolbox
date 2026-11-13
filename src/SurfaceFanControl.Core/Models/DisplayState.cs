using System.Collections.Generic;

namespace SurfaceFanControl.Core.Models
{
    /// <summary>
    /// The built-in display's current mode, as Windows reports it. A null brightness means the
    /// panel does not expose brightness control.
    /// </summary>
    public class DisplayState
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRateHz { get; set; }

        /// <summary>Refresh rates Windows offers at the current resolution, lowest first.</summary>
        public List<int> AvailableRefreshRatesHz { get; set; } = new();

        public int? BrightnessPercent { get; set; }
    }
}
