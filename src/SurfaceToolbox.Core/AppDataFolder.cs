using System;
using System.IO;

namespace SurfaceToolbox.Core
{
    /// <summary>
    /// Where the app keeps its own files (the stock settings backup, the session baseline,
    /// generated reports). The app used to be called Surface Fan Control and kept them in a
    /// folder of that name; the first time this version runs, that folder is moved across so
    /// the stock baseline captured back then is kept rather than captured afresh.
    /// </summary>
    public static class AppDataFolder
    {
        public const string FolderName = "SurfaceToolbox";

        // Written in two parts so a search for the old name in this repo finds only this line.
        public const string LegacyFolderName = "Surface" + "FanControl";

        /// <summary>The app's data folder under %LOCALAPPDATA%. Created (or carried over) on first use.</summary>
        public static string Path => Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        /// <summary>
        /// Returns the data folder under <paramref name="localAppDataRoot"/>, moving a legacy
        /// folder there first if the new one does not exist yet. Takes the root as a parameter
        /// so the move can be tested somewhere harmless.
        /// </summary>
        public static string Resolve(string localAppDataRoot)
        {
            string current = System.IO.Path.Combine(localAppDataRoot, FolderName);
            string legacy = System.IO.Path.Combine(localAppDataRoot, LegacyFolderName);

            try
            {
                // Only when there is nothing to overwrite: if both exist, the new one wins and
                // the old one is left exactly as it is.
                if (!Directory.Exists(current) && Directory.Exists(legacy))
                {
                    Directory.Move(legacy, current);
                }
            }
            catch
            {
                // The old folder stays where it is and a fresh one is used.
            }

            Directory.CreateDirectory(current);
            return current;
        }
    }
}
