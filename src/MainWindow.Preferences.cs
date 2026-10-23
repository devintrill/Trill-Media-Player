using System;
using System.Windows;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private readonly string _settingsPath;
        private readonly DispatcherTimer _savePreferences = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private bool _preferencesReady;

        private void QueuePreferences()
        {
            if (!_preferencesReady) return;
            _savePreferences.Stop(); _savePreferences.Start();
        }

        private void SavePreferences()
        {
            _savePreferences.Stop();
            if (!_preferencesReady) return;
            _settings.SidebarWidth = _sidebarWidth;
            _settings.PlaylistTiles = _playlistTiles;
            Rect bounds = _fullscreen ? _savedBounds : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.WindowWidth = bounds.Width; _settings.WindowHeight = bounds.Height;
            _settings.Maximized = (_fullscreen ? _savedState : WindowState) == WindowState.Maximized;
            string error = SettingsStore.Save(_settings, _settingsPath);
            if (error != null) Status(error);
        }
    }
}
