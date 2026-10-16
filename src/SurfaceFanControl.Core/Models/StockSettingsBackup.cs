using System;

namespace SurfaceFanControl.Core.Models
{
    public class StockSettingsBackup
    {
        public DateTime BackupTimestamp { get; set; } = DateTime.UtcNow;
        public string ActiveSchemeGuid { get; set; } = string.Empty;
        public string EffectiveOverlayGuid { get; set; } = Guid.Empty.ToString();
        public uint SurfaceOverlayAcIndex { get; set; } = 1;
        public uint SurfaceOverlayDcIndex { get; set; } = 2;
        public uint ProcThrottleMaxAc { get; set; } = 100;
        public uint ProcThrottleMaxDc { get; set; } = 100;
        public uint ProcThrottleMinAc { get; set; } = 5;
        public uint ProcThrottleMinDc { get; set; } = 1;
    }
}
