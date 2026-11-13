using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SurfaceToolbox.Core.Models
{
    /// <summary>One recorded reading. Times are UTC.</summary>
    public readonly record struct TelemetrySample(
        DateTime TimestampUtc,
        double PeakTempC,
        double AverageTempC,
        int FanRpm,
        double CpuLoadPercent,
        double CpuMhz,
        bool PluggedIn,
        int BatteryPercent);

    /// <summary>
    /// Keeps the most recent readings in memory, oldest overwritten first. Nothing is written to
    /// disk unless the user exports it, and the history is gone when the app closes.
    /// </summary>
    public sealed class TelemetryHistory
    {
        /// <summary>One hour at one reading a second.</summary>
        public const int DefaultCapacity = 3600;

        private readonly TelemetrySample[] _buffer;
        private readonly object _lock = new();
        private int _next;
        private int _count;

        public TelemetryHistory(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new TelemetrySample[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count
        {
            get { lock (_lock) { return _count; } }
        }

        public void Add(SystemThermalSnapshot snapshot)
        {
            Add(new TelemetrySample(
                snapshot.Timestamp,
                snapshot.PeakTemperatureCelsius,
                snapshot.AverageTemperatureCelsius,
                snapshot.FanRpm,
                snapshot.CpuPerformancePercent,
                snapshot.CpuFrequencyMhz,
                snapshot.IsPluggedIn,
                snapshot.BatteryPercentage));
        }

        public void Add(TelemetrySample sample)
        {
            lock (_lock)
            {
                _buffer[_next] = sample;
                _next = (_next + 1) % _buffer.Length;
                if (_count < _buffer.Length) _count++;
            }
        }

        /// <summary>All recorded readings, oldest first.</summary>
        public TelemetrySample[] Snapshot()
        {
            lock (_lock)
            {
                var result = new TelemetrySample[_count];
                int start = (_next - _count + _buffer.Length) % _buffer.Length;
                for (int i = 0; i < _count; i++)
                    result[i] = _buffer[(start + i) % _buffer.Length];
                return result;
            }
        }

        /// <summary>The readings taken within <paramref name="window"/> of the newest one, oldest first.</summary>
        public TelemetrySample[] Snapshot(TimeSpan window)
        {
            TelemetrySample[] all = Snapshot();
            if (all.Length == 0) return all;

            DateTime cutoff = all[^1].TimestampUtc - window;
            int first = 0;
            while (first < all.Length && all[first].TimestampUtc < cutoff) first++;
            return all[first..];
        }

        /// <summary>
        /// Reduces a series to at most <paramref name="maxPoints"/> values by averaging equal-sized
        /// runs, so a long history can be drawn without one point per pixel being exceeded.
        /// </summary>
        public static double[] Downsample(IReadOnlyList<double> values, int maxPoints)
        {
            if (maxPoints <= 0) throw new ArgumentOutOfRangeException(nameof(maxPoints));
            if (values.Count <= maxPoints)
            {
                var copy = new double[values.Count];
                for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
                return copy;
            }

            var result = new double[maxPoints];
            for (int bucket = 0; bucket < maxPoints; bucket++)
            {
                int from = (int)((long)bucket * values.Count / maxPoints);
                int to = (int)((long)(bucket + 1) * values.Count / maxPoints);
                double sum = 0;
                for (int i = from; i < to; i++) sum += values[i];
                result[bucket] = sum / (to - from);
            }
            return result;
        }

        /// <summary>Writes every recorded reading as CSV, oldest first, with culture-independent numbers.</summary>
        public int WriteCsv(TextWriter writer)
        {
            TelemetrySample[] samples = Snapshot();
            CultureInfo inv = CultureInfo.InvariantCulture;
            writer.WriteLine("time_utc,peak_temp_c,average_temp_c,fan_rpm,cpu_load_percent,cpu_mhz,plugged_in,battery_percent");
            foreach (TelemetrySample s in samples)
            {
                writer.WriteLine(string.Join(",",
                    s.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
                    s.PeakTempC.ToString("0.0", inv),
                    s.AverageTempC.ToString("0.0", inv),
                    s.FanRpm.ToString(inv),
                    s.CpuLoadPercent.ToString("0.#", inv),
                    s.CpuMhz.ToString("0", inv),
                    s.PluggedIn ? "1" : "0",
                    s.BatteryPercent.ToString(inv)));
            }
            return samples.Length;
        }
    }
}
