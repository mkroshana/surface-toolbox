using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace SurfaceFanControl.UI.Services
{
    /// <summary>
    /// Starts the app when the user signs in. The app needs administrator rights, and a normal
    /// startup entry would show a UAC prompt at every sign-in, so this registers a Task Scheduler
    /// task that runs at logon with the highest privileges instead. The task is created with
    /// schtasks.exe from an XML definition, because the settings that matter on a tablet (start
    /// on battery, no time limit) cannot be set from the schtasks command line.
    /// </summary>
    public sealed class AutostartService
    {
        public const string TaskName = "SurfaceFanControl Autostart";

        /// <summary>Passed to the app by the task so it starts in the tray instead of opening its window.</summary>
        public const string TrayArgument = "--tray";

        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        /// <summary>The program the registered task starts, or null if there is no such task.</summary>
        public string? GetRegisteredCommand()
        {
            (int exitCode, string output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
            if (exitCode != 0)
                return null;

            try
            {
                int start = output.IndexOf("<?xml", StringComparison.Ordinal);
                XDocument document = XDocument.Parse(start >= 0 ? output.Substring(start) : output);
                string? command = document.Root?.Element(Ns + "Actions")?.Element(Ns + "Exec")?.Element(Ns + "Command")?.Value;
                return command?.Trim('"');
            }
            catch
            {
                return string.Empty; // The task exists but could not be read.
            }
        }

        public bool IsEnabled() => GetRegisteredCommand() != null;

        /// <summary>Registers (or replaces) the task so it starts <paramref name="executablePath"/> in the tray at sign-in.</summary>
        public bool Enable(string executablePath, out string error)
        {
            error = string.Empty;
            string definitionPath = Path.Combine(Path.GetTempPath(), $"surfacefancontrol-autostart-{Environment.ProcessId}.xml");
            try
            {
                // Task Scheduler reads the definition as UTF-16, matching the declaration it carries.
                XDocument definition = BuildDefinition(executablePath);
                File.WriteAllText(definitionPath, definition.Declaration + Environment.NewLine + definition, Encoding.Unicode);
                (int exitCode, string output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{definitionPath}\" /F");
                if (exitCode != 0)
                {
                    error = output.Trim();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { File.Delete(definitionPath); } catch { }
            }
        }

        public bool Disable(out string error)
        {
            error = string.Empty;
            if (!IsEnabled())
                return true;

            (int exitCode, string output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            if (exitCode != 0)
            {
                error = output.Trim();
                return false;
            }
            return true;
        }

        // Public so the definition can be checked without registering anything.
        public static XDocument BuildDefinition(string executablePath)
        {
            string user = WindowsIdentity.GetCurrent().Name;
            return new XDocument(
                new XDeclaration("1.0", "UTF-16", null),
                new XElement(Ns + "Task", new XAttribute("version", "1.2"),
                    new XElement(Ns + "RegistrationInfo",
                        new XElement(Ns + "Description", "Starts Surface Fan Control in the tray when you sign in. Created by the app's Settings page; turn it off there.")),
                    new XElement(Ns + "Triggers",
                        new XElement(Ns + "LogonTrigger",
                            new XElement(Ns + "Enabled", "true"),
                            new XElement(Ns + "UserId", user))),
                    new XElement(Ns + "Principals",
                        new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                            new XElement(Ns + "UserId", user),
                            new XElement(Ns + "LogonType", "InteractiveToken"),
                            new XElement(Ns + "RunLevel", "HighestAvailable"))),
                    new XElement(Ns + "Settings",
                        new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        // A tablet is often on battery at sign-in; the defaults would silently skip the start.
                        new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(Ns + "AllowHardTerminate", "true"),
                        new XElement(Ns + "StartWhenAvailable", "false"),
                        new XElement(Ns + "AllowStartOnDemand", "true"),
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "Hidden", "false"),
                        // PT0S = no limit; the default would end the app after three days.
                        new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(Ns + "Priority", "5")),
                    new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                        new XElement(Ns + "Exec",
                            new XElement(Ns + "Command", $"\"{executablePath}\""),
                            new XElement(Ns + "Arguments", TrayArgument)))));
        }

        private static (int ExitCode, string Output) RunSchtasks(string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process? process = Process.Start(start);
            if (process == null)
                return (-1, "schtasks could not be started.");

            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, output);
        }
    }
}
