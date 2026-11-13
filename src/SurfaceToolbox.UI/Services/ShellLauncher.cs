using System;
using System.Diagnostics;
using System.IO;

namespace SurfaceToolbox.UI.Services
{
    /// <summary>
    /// Opens files and links for the user. The app itself runs elevated, and anything it starts
    /// directly inherits that: a browser opened that way would run with administrator rights.
    /// Handing the target to explorer.exe instead makes the already-running desktop shell open
    /// it, as the signed-in user without elevation.
    /// </summary>
    public static class ShellLauncher
    {
        /// <summary>Opens a file path, a web address, or a protocol link such as mailto: or ms-settings:.</summary>
        public static void OpenUnelevated(string target)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"\"{target}\"",
                UseShellExecute = false
            };
            using Process? process = Process.Start(start);
        }
    }
}
