using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows.Input;
using Trill;

public static class SettingsTests
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        try
        {
            TestDefaultShortcuts();
            TestCanceledEditIsolation();
            TestShortcutAssignment();
            TestReservedKeys();
            TestLocalPreferences();

            Console.WriteLine("PASS: " + assertions + " settings and privacy assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static void TestDefaultShortcuts()
    {
        AppSettings settings = AppSettings.CreateDefault();
        Assert(settings.Shortcuts.Count == Enum.GetValues(typeof(PlayerAction)).Length, "Every player action should have a default shortcut");
        Assert(settings.Shortcuts[PlayerAction.TogglePlaylist].Matches(Key.L, ModifierKeys.Control), "Ctrl+L should toggle the playlist");
        HashSet<string> combinations = new HashSet<string>();
        foreach (KeyValuePair<PlayerAction, ShortcutBinding> pair in settings.Shortcuts)
        {
            Assert(!pair.Value.Matches(Key.Tab, ModifierKeys.None), "Unmodified Tab must remain available for keyboard focus navigation");
            Assert(combinations.Add(((int)pair.Value.Key).ToString() + ":" + ((int)pair.Value.Modifiers).ToString()), "Default shortcuts must not conflict");
        }
        Assert(settings.Shortcuts[PlayerAction.TogglePlayPause].Matches(Key.Space, ModifierKeys.None), "Space should play or pause");
        Assert(!settings.Shortcuts[PlayerAction.TogglePlayPause].Matches(Key.Space, ModifierKeys.Control), "Extra modifiers must not accidentally trigger another action");
        Assert(!settings.Shortcuts[PlayerAction.Next].Matches(Key.Right, ModifierKeys.None), "Seeking must not accidentally trigger next-item navigation");
        Assert(settings.Shortcuts[PlayerAction.SeekForward].Matches(Key.Right, ModifierKeys.None), "Unmodified Right should seek forward");
    }

    private static void TestCanceledEditIsolation()
    {
        AppSettings active = AppSettings.CreateDefault();
        AppSettings draft = active.Clone();
        draft.Shortcuts[PlayerAction.TogglePlayPause].Key = Key.P;
        draft.Shortcuts[PlayerAction.Next].Modifiers = ModifierKeys.Alt;
        draft.Volume = 0.1;
        draft.PlaybackSpeed = 2;
        draft.LoopPlaylist = true;
        draft.ShowPlaylist = false;
        draft.IsMuted = true;
        draft.UseWindowsFileDialogs = false;
        Assert(active.UseWindowsFileDialogs, "Canceling an edit must preserve the active picker choice");
        Assert(!draft.Clone().UseWindowsFileDialogs, "Picker preference survives settings cloning");
        Assert(active.Shortcuts[PlayerAction.TogglePlayPause].Key == Key.Space, "Discarding a shortcut edit must leave the active key intact");
        Assert(active.Shortcuts[PlayerAction.Next].Modifiers == ModifierKeys.Control, "Draft modifiers must not alter active modifiers");
        Assert(active.Volume == 0.75 && active.PlaybackSpeed == 1, "Draft playback values must not alter the active session");
        Assert(!active.LoopPlaylist && active.ShowPlaylist && !active.IsMuted, "Draft toggles must not alter the active session");
        draft.Shortcuts.Clear();
        Assert(active.Shortcuts.Count == Enum.GetValues(typeof(PlayerAction)).Length, "Clearing a draft must not clear active shortcuts");
    }

    private static void TestShortcutAssignment()
    {
        AppSettings settings = AppSettings.CreateDefault();
        string error;
        Assert(!ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.Space, ModifierKeys.None, out error), "Assigning a shortcut owned by another action must fail");
        Assert(!String.IsNullOrEmpty(error) && error.Contains("play / pause"), "A conflict should identify the action already using the shortcut");
        Assert(settings.Shortcuts[PlayerAction.Next].Matches(Key.Right, ModifierKeys.Control), "A rejected assignment must preserve the previous shortcut");
        Assert(ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.Right, ModifierKeys.Control, out error), "Reassigning an action to its existing shortcut should succeed");
        Assert(ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.Space, ModifierKeys.Shift, out error), "Different modifiers should allow distinct shortcuts on the same key");
        Assert(settings.Shortcuts[PlayerAction.Next].Matches(Key.Space, ModifierKeys.Shift), "Assigned modifiers should match exactly");
        Assert(!settings.Shortcuts[PlayerAction.Next].Matches(Key.Space, ModifierKeys.Shift | ModifierKeys.Control), "A modifier superset must not match");
        Assert(ShortcutRules.TryAssign(settings, PlayerAction.TogglePlayPause, Key.None, ModifierKeys.None, out error), "Users should be able to remove a shortcut");
        Assert(!settings.Shortcuts[PlayerAction.TogglePlayPause].Matches(Key.None, ModifierKeys.None), "An unassigned shortcut must never trigger");
        Assert(ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.Space, ModifierKeys.None, out error), "A cleared shortcut should become available to another action");
        Assert(ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.P, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, out error), "Supported combined modifiers should be assignable");
        Assert(settings.Shortcuts[PlayerAction.Next].Display == "Ctrl + Alt + Shift + P", "The displayed combination should describe the assignment accurately");
    }

    private static void TestReservedKeys()
    {
        Key[] invalidKeys = {
            Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift,
            Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin,
            Key.System, Key.ImeProcessed, Key.DeadCharProcessed, Key.Escape, (Key)Int32.MaxValue
        };
        AppSettings settings = AppSettings.CreateDefault();
        string error;
        foreach (Key key in invalidKeys)
        {
            Assert(!ShortcutRules.TryAssign(settings, PlayerAction.Next, key, ModifierKeys.None, out error), "Modifier-only, reserved, or nonphysical keys cannot become shortcuts: " + key);
            Assert(settings.Shortcuts[PlayerAction.Next].Matches(Key.Right, ModifierKeys.Control), "Rejected keys must leave the existing shortcut intact");
        }
        ShortcutBinding[] reserved = {
            new ShortcutBinding(Key.F4, ModifierKeys.Alt),
            new ShortcutBinding(Key.Tab, ModifierKeys.Alt),
            new ShortcutBinding(Key.Space, ModifierKeys.Alt),
            new ShortcutBinding(Key.Delete, ModifierKeys.Control | ModifierKeys.Alt),
            new ShortcutBinding(Key.L, ModifierKeys.Windows)
        };
        foreach (ShortcutBinding binding in reserved)
            Assert(!ShortcutRules.TryAssign(settings, PlayerAction.Next, binding.Key, binding.Modifiers, out error), "Windows-reserved combinations should not be offered as working app shortcuts: " + binding.Display);
        Assert(!ShortcutRules.TryAssign(settings, PlayerAction.Next, Key.P, (ModifierKeys)16, out error), "Undefined modifier flags must be rejected");
    }

    private static void TestLocalPreferences()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "preferences-test.txt");
        AppSettings settings = AppSettings.CreateDefault();
        settings.SidebarWidth = 380; settings.PlaylistTiles = true; settings.ShowPlaylist = false;
        settings.WindowWidth = 1400; settings.WindowHeight = 900; settings.Maximized = true;
        settings.PlaylistSort = 2; settings.Volume = .4; settings.UseWindowsFileDialogs = false;
        settings.Shortcuts[PlayerAction.TogglePlayPause] = new ShortcutBinding(Key.P, ModifierKeys.Control);
        settings.Shortcuts[PlayerAction.FrameForward] = new ShortcutBinding(Key.None, ModifierKeys.None);
        Assert(SettingsStore.Save(settings, path) == null, "Preferences save locally");
        AppSettings loaded = SettingsStore.Load(path);
        Assert(loaded.SidebarWidth == 380 && loaded.PlaylistTiles && !loaded.ShowPlaylist, "Sidebar layout survives reopening");
        Assert(loaded.WindowWidth == 1400 && loaded.WindowHeight == 900 && loaded.Maximized, "Window layout survives reopening");
        Assert(loaded.PlaylistSort == 2 && loaded.Volume == .4 && !loaded.UseWindowsFileDialogs, "Sort and playback preferences survive reopening");
        Assert(loaded.Shortcuts[PlayerAction.TogglePlayPause].Matches(Key.P, ModifierKeys.Control), "Custom shortcut survives reopening");
        Assert(loaded.Shortcuts[PlayerAction.FrameForward].Key == Key.None, "Unassigned shortcut remains unassigned");
        loaded.SidebarWidth = 420;
        Assert(SettingsStore.Save(loaded, path) == null && SettingsStore.Load(path).SidebarWidth == 420, "Replacing saved preferences preserves the latest value");
        System.IO.File.WriteAllText(path, "Volume=NaN\nSidebarWidth=-500\nWindowWidth=Infinity\nKey.Next=999999,9999\nTiles=broken");
        loaded = SettingsStore.Load(path);
        Assert(loaded.Volume == .75 && loaded.SidebarWidth == 210 && loaded.WindowWidth == 1240, "Invalid numeric preferences fall back or clamp safely");
        Assert(!loaded.PlaylistTiles && loaded.Shortcuts[PlayerAction.Next].Matches(Key.Right, ModifierKeys.Control), "Malformed preferences cannot introduce invalid shortcuts");
        System.IO.File.Delete(path);
        Assert(SettingsStore.Load(path).ShowPlaylist, "Missing preferences load defaults");
        Assert(AppSettings.CreateDefault().Shortcuts[PlayerAction.FrameForward].Matches(Key.OemCloseBrackets, ModifierKeys.None), "Right bracket advances a frame by default");
        Assert(AppSettings.CreateDefault().Shortcuts[PlayerAction.FrameBackward].Matches(Key.OemOpenBrackets, ModifierKeys.None), "Left bracket reverses a frame by default");
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
}
