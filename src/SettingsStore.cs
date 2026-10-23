using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Trill
{
    // Local preferences only; no media paths or search terms are stored.
    public static class SettingsStore
    {
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trill", "settings.txt"); } }
        public static AppSettings Load(string storagePath = null)
        {
            var settings = AppSettings.CreateDefault();
            try
            {
                string path = storagePath ?? FilePath;
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return settings;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOf('=');
                    if (split > 0) values[line.Substring(0, split)] = line.Substring(split + 1);
                }
                settings.Volume = Number(values, "Volume", .75, 0, 1);
                settings.PlaybackSpeed = Number(values, "Speed", 1, .25, 4);
                settings.SidebarWidth = Number(values, "SidebarWidth", 240, 210, 520);
                settings.WindowWidth = Number(values, "WindowWidth", 1240, 940, 7680);
                settings.WindowHeight = Number(values, "WindowHeight", 800, 610, 4320);
                settings.PlaylistSort = (int)Number(values, "Sort", 0, 0, 3);
                settings.ShowPlaylist = Flag(values, "Sidebar", true);
                settings.PlaylistTiles = Flag(values, "Tiles", false);
                settings.Maximized = Flag(values, "Maximized", false);
                settings.IsMuted = Flag(values, "Muted", false);
                settings.LoopPlaylist = Flag(values, "Wrap", false);
                settings.UseWindowsFileDialogs = Flag(values, "WindowsPicker", true);
                var defaults = settings.Clone();
                foreach (PlayerAction action in Enum.GetValues(typeof(PlayerAction)))
                    settings.Shortcuts[action] = new ShortcutBinding(Key.None, ModifierKeys.None);
                foreach (PlayerAction action in Enum.GetValues(typeof(PlayerAction)))
                {
                    string saved, error;
                    int key, modifiers;
                    if (values.TryGetValue("Key." + action, out saved))
                    {
                        string[] parts = saved.Split(',');
                        if (parts.Length == 2 && Int32.TryParse(parts[0], out key) && Int32.TryParse(parts[1], out modifiers) &&
                            ShortcutRules.TryAssign(settings, action, (Key)key, (ModifierKeys)modifiers, out error)) continue;
                    }
                    ShortcutBinding original = defaults.Shortcuts[action];
                    ShortcutRules.TryAssign(settings, action, original.Key, original.Modifiers, out error);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return settings;
        }
        private static double Number(Dictionary<string,string> values, string key, double fallback, double min, double max)
        {
            string text; double value;
            return values.TryGetValue(key, out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !Double.IsNaN(value) && !Double.IsInfinity(value) ? Math.Max(min, Math.Min(max, value)) : fallback;
        }
        private static bool Flag(Dictionary<string,string> values, string key, bool fallback)
        {
            string text; bool value;
            return values.TryGetValue(key, out text) && Boolean.TryParse(text, out value) ? value : fallback;
        }
        public static string Save(AppSettings settings, string storagePath = null)
        {
            string pending = null;
            try
            {
                string path = storagePath ?? FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var values = new Dictionary<string,object> {
                    {"Volume", settings.Volume}, {"Speed", settings.PlaybackSpeed}, {"Muted", settings.IsMuted},
                    {"Wrap", settings.LoopPlaylist}, {"WindowsPicker", settings.UseWindowsFileDialogs},
                    {"Sidebar", settings.ShowPlaylist}, {"SidebarWidth", settings.SidebarWidth}, {"Tiles", settings.PlaylistTiles},
                    {"Sort", settings.PlaylistSort}, {"WindowWidth", settings.WindowWidth}, {"WindowHeight", settings.WindowHeight},
                    {"Maximized", settings.Maximized}
                };
                foreach (var pair in settings.Shortcuts)
                    values["Key." + pair.Key] = (int)pair.Value.Key + "," + (int)pair.Value.Modifiers;
                pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllLines(pending, values.Select(p => p.Key + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                return null;
            }
            catch (IOException) { return "Unable to save preferences"; }
            catch (UnauthorizedAccessException) { return "Unable to save preferences"; }
            finally
            {
                if (pending != null) try { if (File.Exists(pending)) File.Delete(pending); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        internal static bool IsBindableKey(Key key) { return ShortcutRules.IsBindableKey(key); }
    }
}