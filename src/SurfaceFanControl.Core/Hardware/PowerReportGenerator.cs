using System;
using System.Diagnostics;
using System.IO;

namespace SurfaceFanControl.Core.Hardware
{
    public enum PowerReportKind
    {
        /// <summary>powercfg /batteryreport: capacity history, recent usage and life estimates.</summary>
        Battery,

        /// <summary>powercfg /sleepstudy: what drained the battery during Modern Standby sleep. Needs administrator rights.</summary>
        SleepStudy
    }

    public class PowerReportResult
    {
        public bool Succeeded { get; set; }
        public string Path { get; set; } = string.Empty;

        /// <summary>What powercfg printed, for the case where it failed.</summary>
        public string Output { get; set; } = string.Empty;
    }

    /// <summary>
    /// Has Windows write one of its own power reports to an HTML file. The reports are produced
    /// entirely by powercfg.exe; nothing in them is computed here.
    /// </summary>
    public sealed class PowerReportGenerator
    {
        private const int TimeoutMs = 90000;

        public PowerReportResult Generate(PowerReportKind kind, string outputPath)
        {
            var result = new PowerReportResult { Path = outputPath };
            try
            {
                string? folder = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                string verb = kind == PowerReportKind.Battery ? "/batteryreport" : "/sleepstudy";
                var start = new ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                    Arguments = $"{verb} /output \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(start);
                if (process == null)
                {
                    result.Output = "powercfg could not be started.";
                    return result;
                }

                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(TimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    result.Output = "powercfg did not finish in time.";
                    return result;
                }

                result.Output = output.Trim();
                result.Succeeded = process.ExitCode == 0 && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                result.Output = ex.Message;
            }
            return result;
        }
    }
}
