using System;
using System.ComponentModel;
using SurfaceToolbox.UI.Services;

namespace SurfaceToolbox.UI.ViewModels
{
    /// <summary>Backs the Settings page. For now that is one thing: whether the app starts when you sign in.</summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly AutostartService _autostart;
        private readonly string _executablePath;
        private bool _startWithWindows;
        private string _startupStatus = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SettingsViewModel(AutostartService? autostart = null, string? executablePath = null)
        {
            _autostart = autostart ?? new AutostartService();
            _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
            // The state is read when the page is first shown (Refresh), not here, so opening the
            // app does not pay for a Task Scheduler query nobody asked for.
        }

        /// <summary>Whether the sign-in task exists. Setting it creates or removes the task.</summary>
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (value == _startWithWindows) return;

                string error;
                bool ok = value ? _autostart.Enable(_executablePath, out error) : _autostart.Disable(out error);
                Refresh();
                if (!ok)
                    StartupStatus = $"Could not change the startup setting: {error}";
            }
        }

        public string StartupStatus
        {
            get => _startupStatus;
            private set { _startupStatus = value; Raise(nameof(StartupStatus)); }
        }

        /// <summary>Re-reads the real state from Task Scheduler, so the switch never shows a guess.</summary>
        public void Refresh()
        {
            string? command = _autostart.GetRegisteredCommand();
            _startWithWindows = command != null;
            Raise(nameof(StartWithWindows));

            if (command == null)
                StartupStatus = "Off. The app starts only when you open it.";
            else if (command.Length == 0 || string.Equals(command, _executablePath, StringComparison.OrdinalIgnoreCase))
                StartupStatus = "On. The app starts in the tray when you sign in.";
            else
                StartupStatus = $"On, but for a different copy of the app: {command}. Turn it off and on again to use this copy.";
        }

        private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
