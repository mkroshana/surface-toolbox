namespace SurfaceFanControl.Core.Models
{
    /// <summary>
    /// The power settings as they were before the app first changed them. Written to disk when
    /// the first change is made and removed once the settings are put back, so a run that ends
    /// without a clean exit can still be undone by the next one. A null value means the setting
    /// could not be read, or the app has not changed it, and it is left alone on restore.
    /// </summary>
    public class SessionBaseline
    {
        public string SchemeGuid { get; set; } = string.Empty;
        public uint? SurfaceOverlayAcIndex { get; set; }
        public uint? SurfaceOverlayDcIndex { get; set; }
        public uint? ProcThrottleMaxAc { get; set; }
        public uint? ProcThrottleMaxDc { get; set; }

        /// <summary>Power mode overlay while plugged in, before the app changed it.</summary>
        public string? AcOverlayGuid { get; set; }

        /// <summary>Power mode overlay while on battery, before the app changed it.</summary>
        public string? DcOverlayGuid { get; set; }
    }
}
