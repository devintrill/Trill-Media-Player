using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Trill
{
    public enum PlayerAction
    {
        TogglePlayPause, Next, Previous, SeekForward, SeekBackward,
        SpeedUp, SpeedDown, ResetSpeed, VolumeUp, VolumeDown,
        ToggleMute, ToggleFullscreen, OpenFiles, OpenFolder, TogglePlaylist, FrameForward, FrameBackward, ToggleFocus,
        ToggleAbLoop, ExportFrame
    }

    public static class PlayerActionNames
    {
        public static string GetLabel(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.TogglePlayPause: return "Play / pause";
                case PlayerAction.Next: return "Next item";
                case PlayerAction.Previous: return "Previous item";
                case PlayerAction.SeekForward: return "Forward 10 seconds";
                case PlayerAction.SeekBackward: return "Back 10 seconds";
                case PlayerAction.SpeedUp: return "Increase playback speed";
                case PlayerAction.SpeedDown: return "Decrease playback speed";
                case PlayerAction.ResetSpeed: return "Reset playback speed";
                case PlayerAction.VolumeUp: return "Increase volume";
                case PlayerAction.VolumeDown: return "Decrease volume";
                case PlayerAction.ToggleMute: return "Mute / unmute";
                case PlayerAction.ToggleFullscreen: return "Enter / exit full screen";
                case PlayerAction.OpenFiles: return "Open files";
                case PlayerAction.OpenFolder: return "Open folder";
                case PlayerAction.TogglePlaylist: return "Show / hide playlist";
                case PlayerAction.ToggleAbLoop: return "Loop a section (A-B)";
                case PlayerAction.ExportFrame: return "Save current frame as a picture";
                case PlayerAction.FrameForward: return "Next frame";
                case PlayerAction.FrameBackward: return "Previous frame";
                case PlayerAction.ToggleFocus: return "Focus mode";
                default: return action.ToString();
            }
        }
    }

    public sealed class ShortcutBinding
    {
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }

        public ShortcutBinding() { }

        public ShortcutBinding(Key key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public bool Matches(KeyEventArgs e)
        {
            Key pressedKey = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            return Matches(pressedKey, e.KeyboardDevice.Modifiers);
        }

        public bool Matches(Key pressedKey, ModifierKeys modifiers)
        {
            return Key != System.Windows.Input.Key.None && pressedKey == Key && modifiers == Modifiers;
        }

        public ShortcutBinding Clone()
        {
            return new ShortcutBinding(Key, Modifiers);
        }

        public string Display
        {
            get
            {
                if (Key == System.Windows.Input.Key.None) return "Unassigned";
                string prefix = "";
                if ((Modifiers & ModifierKeys.Control) != 0) prefix += "Ctrl + ";
                if ((Modifiers & ModifierKeys.Alt) != 0) prefix += "Alt + ";
                if ((Modifiers & ModifierKeys.Shift) != 0) prefix += "Shift + ";
                if ((Modifiers & ModifierKeys.Windows) != 0) prefix += "Win + ";
                return prefix + KeyName(Key);
            }
        }

        public override string ToString() { return Display; }

        private static string KeyName(Key key)
        {
            switch (key)
            {
                case System.Windows.Input.Key.OemPeriod: return ".";
                case System.Windows.Input.Key.OemComma: return ",";
                case System.Windows.Input.Key.OemQuestion: return "/";
                case System.Windows.Input.Key.OemPlus: return "+";
                case System.Windows.Input.Key.OemMinus: return "-";
                case System.Windows.Input.Key.OemOpenBrackets: return "[";
                case System.Windows.Input.Key.OemCloseBrackets: return "]";
                case System.Windows.Input.Key.OemSemicolon: return ";";
                case System.Windows.Input.Key.OemQuotes: return "'";
                case System.Windows.Input.Key.OemTilde: return "`";
                case System.Windows.Input.Key.OemBackslash: return "\\";
                case System.Windows.Input.Key.Return: return "Enter";
                case System.Windows.Input.Key.Back: return "Backspace";
                case System.Windows.Input.Key.Escape: return "Esc";
                case System.Windows.Input.Key.Prior: return "Page Up";
                case System.Windows.Input.Key.Next: return "Page Down";
            }
            string name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && Char.IsDigit(name[1])) return name.Substring(1);
            return name;
        }
    }

    internal static class ShortcutRules
    {
        internal static bool IsBindableKey(Key key)
        {
            return Enum.IsDefined(typeof(Key), key) && key != Key.None &&
                key != Key.LeftCtrl && key != Key.RightCtrl &&
                key != Key.LeftAlt && key != Key.RightAlt && key != Key.LeftShift && key != Key.RightShift &&
                key != Key.LWin && key != Key.RWin && key != Key.System && key != Key.ImeProcessed &&
                key != Key.DeadCharProcessed && key != Key.Escape;
        }

        internal static bool TryAssign(AppSettings settings, PlayerAction action, Key key, ModifierKeys modifiers, out string error)
        {
            error = null;
            if (settings == null || !Enum.IsDefined(typeof(PlayerAction), action))
            {
                error = "Choose an available player action.";
                return false;
            }
            if (key == Key.None)
            {
                settings.Shortcuts[action] = new ShortcutBinding(Key.None, ModifierKeys.None);
                return true;
            }
            if (!IsBindableKey(key))
            {
                error = "Choose a key other than a modifier or Escape.";
                return false;
            }
            ModifierKeys allowedModifiers = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows;
            if ((modifiers & ~allowedModifiers) != 0)
            {
                error = "That modifier combination is not supported.";
                return false;
            }
            bool alt = (modifiers & ModifierKeys.Alt) != 0;
            bool control = (modifiers & ModifierKeys.Control) != 0;
            if ((modifiers & ModifierKeys.Windows) != 0 ||
                (alt && (key == Key.F4 || key == Key.Tab || key == Key.Space)) ||
                (alt && control && key == Key.Delete))
            {
                error = "That combination is reserved by Windows. Choose another shortcut.";
                return false;
            }
            ShortcutBinding proposed = new ShortcutBinding(key, modifiers);
            foreach (KeyValuePair<PlayerAction, ShortcutBinding> pair in settings.Shortcuts)
            {
                if (pair.Key != action && pair.Value != null && pair.Value.Key == key && pair.Value.Modifiers == modifiers)
                {
                    error = proposed.Display + " is already used for " + PlayerActionNames.GetLabel(pair.Key).ToLowerInvariant() +
                        ". Choose another combination or clear that shortcut first.";
                    return false;
                }
            }
            settings.Shortcuts[action] = proposed;
            return true;
        }
    }
    public sealed class AppSettings
    {
        public Dictionary<PlayerAction, ShortcutBinding> Shortcuts { get; set; }
        public double PlaybackSpeed { get; set; }
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public bool LoopPlaylist { get; set; }
        public bool ShowPlaylist { get; set; }
        public bool UseWindowsFileDialogs { get; set; }

        public double SidebarWidth { get; set; }
        public bool PlaylistTiles { get; set; }
        public int PlaylistSort { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool Maximized { get; set; }

        public AppSettings()
        {
            Shortcuts = new Dictionary<PlayerAction, ShortcutBinding>();
            SidebarWidth = 240; WindowWidth = 1240; WindowHeight = 800;
            PlaybackSpeed = 1;
            Volume = 0.75;
            ShowPlaylist = true;
            UseWindowsFileDialogs = true;
        }

        public static AppSettings CreateDefault()
        {
            AppSettings settings = new AppSettings();
            settings.Shortcuts[PlayerAction.TogglePlayPause] = new ShortcutBinding(Key.Space, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.Next] = new ShortcutBinding(Key.Right, ModifierKeys.Control);
            settings.Shortcuts[PlayerAction.Previous] = new ShortcutBinding(Key.Left, ModifierKeys.Control);
            settings.Shortcuts[PlayerAction.SeekForward] = new ShortcutBinding(Key.Right, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.SeekBackward] = new ShortcutBinding(Key.Left, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.SpeedUp] = new ShortcutBinding(Key.OemPeriod, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.SpeedDown] = new ShortcutBinding(Key.OemComma, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.ResetSpeed] = new ShortcutBinding(Key.OemQuestion, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.VolumeUp] = new ShortcutBinding(Key.Up, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.VolumeDown] = new ShortcutBinding(Key.Down, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.ToggleMute] = new ShortcutBinding(Key.M, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.ToggleFullscreen] = new ShortcutBinding(Key.F, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.OpenFiles] = new ShortcutBinding(Key.O, ModifierKeys.Control);
            settings.Shortcuts[PlayerAction.OpenFolder] = new ShortcutBinding(Key.O, ModifierKeys.Control | ModifierKeys.Shift);
            settings.Shortcuts[PlayerAction.TogglePlaylist] = new ShortcutBinding(Key.L, ModifierKeys.Control);
            settings.Shortcuts[PlayerAction.FrameForward] = new ShortcutBinding(Key.OemCloseBrackets, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.FrameBackward] = new ShortcutBinding(Key.OemOpenBrackets, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.ToggleFocus] = new ShortcutBinding(Key.F, ModifierKeys.Control | ModifierKeys.Shift);
            settings.Shortcuts[PlayerAction.ToggleAbLoop] = new ShortcutBinding(Key.L, ModifierKeys.None);
            settings.Shortcuts[PlayerAction.ExportFrame] = new ShortcutBinding(Key.S, ModifierKeys.Control);
            return settings;
        }

        public AppSettings Clone()
        {
            AppSettings copy = new AppSettings();
            copy.SidebarWidth = SidebarWidth; copy.PlaylistTiles = PlaylistTiles; copy.PlaylistSort = PlaylistSort;
            copy.WindowWidth = WindowWidth; copy.WindowHeight = WindowHeight; copy.Maximized = Maximized;
            copy.PlaybackSpeed = PlaybackSpeed;
            copy.Volume = Volume;
            copy.IsMuted = IsMuted;
            copy.LoopPlaylist = LoopPlaylist;
            copy.ShowPlaylist = ShowPlaylist;
            copy.UseWindowsFileDialogs = UseWindowsFileDialogs;
            foreach (KeyValuePair<PlayerAction, ShortcutBinding> pair in Shortcuts)
                copy.Shortcuts[pair.Key] = pair.Value.Clone();
            return copy;
        }
    }
}
