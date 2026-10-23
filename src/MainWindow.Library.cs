using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Trill
{
    internal sealed class SidebarEntry
    {
        public string Path { get; private set; }
        public MediaItem Media { get; private set; }
        public bool IsFolder { get { return Media == null; } }
        public string Name { get { return IsFolder ? System.IO.Path.GetFileName(Path.TrimEnd('\\')) : Media.Name; } }
        public string Kind { get { return IsFolder ? "Folder" : Media.Kind; } }
        public string Detail { get { return IsFolder ? "Folder" : Media.Detail; } }
        public DateTime Modified { get; private set; }
        private void ReadModified()
        {
            try { Modified = File.GetLastWriteTimeUtc(Path); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public ThumbnailHandle Thumb { get { return Media == null ? null : Media.Thumb; } }
        public SidebarEntry(string path) { Path = path; ReadModified(); }
        public SidebarEntry(MediaItem media) { Path = media.Path; Media = media; ReadModified(); }
    }

    internal static class FavoritesStore
    {
        internal static string FilePath { get { return System.IO.Path.Combine(RecentStore.FolderPath, "favorites.txt"); } }
        internal static HashSet<string> Load(string storagePath = null)
        {
            storagePath = storagePath ?? FilePath;
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(storagePath))
                    foreach (string path in File.ReadLines(storagePath).Take(10000))
                        if (MediaFiles.IsLocalPath(path)) result.Add(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }
        internal static bool Save(HashSet<string> paths, string storagePath = null)
        {
            try
            {
                storagePath = storagePath ?? FilePath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(storagePath));
                string pending = storagePath + ".tmp";
                File.WriteAllLines(pending, paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                if (File.Exists(storagePath)) File.Replace(pending, storagePath, null);
                else File.Move(pending, storagePath);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public sealed partial class MainWindow
    {
        private readonly ObservableCollection<SidebarEntry> _sidebarEntries = new ObservableCollection<SidebarEntry>();
        private readonly HashSet<string> _favorites = FavoritesStore.Load();
        private readonly Random _random = new Random();
        private readonly List<MediaItem> _shuffleHistory = new List<MediaItem>();
        private readonly HashSet<MediaItem> _shuffleVisited = new HashSet<MediaItem>();
        private string _browserFolder;
        private int _browserVersion, _repeatMode = 1;
        private bool _showingFavorites, _libraryClosed, _shuffle, _playlistTiles;

        private void SetupLibrary()
        {
            _playlist.ItemsSource = _sidebarEntries;
            SetupBrowsing();
            Click("PlaylistLayoutButton", delegate { TogglePlaylistLayout(); });
            Click("UpFolderButton", delegate
            {
                if (_browserFolder == null) return;
                DirectoryInfo parent = Directory.GetParent(_browserFolder);
                if (parent != null) BrowseSidebar(parent.FullName, false);
            });
            Click("FavoritesButton", delegate { ShowFavorites(); });
            Click("FavoriteButton", delegate { if (_current != null) ToggleFavorite(_current.Path); });
            Click("LoopButton", delegate { _repeatMode = (_repeatMode + 1) % 3; UpdateModes(); });
            Click("ShuffleButton", delegate { _shuffle = !_shuffle; _shuffleHistory.Clear(); _shuffleVisited.Clear(); UpdateControls(); });
            _playlist.ContextMenu = new ContextMenu();
            _playlist.ContextMenuOpening += delegate(object sender, ContextMenuEventArgs e)
            {
                _playlist.ContextMenu.Items.Clear();
                SidebarEntry row = _playlist.SelectedItem as SidebarEntry;
                if (e.CursorLeft >= 0)
                {
                    var hit = _playlist.InputHitTest(Mouse.GetPosition(_playlist)) as DependencyObject;
                    var container = hit == null ? null : ItemsControl.ContainerFromElement(_playlist, hit) as ListBoxItem;
                    row = container == null ? null : container.Content as SidebarEntry;
                }
                if (row == null) { e.Handled = true; return; }
                var favorite = new MenuItem { Header = _favorites.Contains(row.Path) ? "Remove favorite" : "Favorite" };
                favorite.Click += delegate { ToggleFavorite(row.Path); };
                _playlist.ContextMenu.Items.Add(favorite);
            };
            Closed += delegate { _libraryClosed = true; _browserVersion++; };
            UpdateModes();
        }

        private void TogglePlaylistLayout()
        {
            _playlistTiles = !_playlistTiles;
            FrameworkElementFactory panel = new FrameworkElementFactory(_playlistTiles ? typeof(VirtualizingTilePanel) : typeof(VirtualizingStackPanel));
            if (_playlistTiles)
            {
                panel.SetValue(VirtualizingTilePanel.FixedColumnsProperty, 2);
                panel.SetValue(VirtualizingTilePanel.ItemHeightProperty, 144.0);
            }
            _playlist.ItemsPanel = new ItemsPanelTemplate(panel);
            _playlist.ItemTemplate = (DataTemplate)_root.Resources[_playlistTiles ? "PlaylistTileTemplate" : "PlaylistRowTemplate"];
            Button button = Find<Button>("PlaylistLayoutButton");
            button.Content = _playlistTiles ? "\uE8FD" : "\uE8A9";
            button.ToolTip = _playlistTiles ? "List view" : "Two-column thumbnails";
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
            if (_playlistTiles && _sidebarWidth < 320)
            {
                _sidebarWidth = 320;
                SetPlaylistVisible();
            }
            Dispatcher.BeginInvoke(new Action(SelectCurrentRow)); QueuePreferences();
        }
        private void UpdateModes()
        {
            Button loop = Find<Button>("LoopButton");
            loop.Content = _repeatMode == 1 ? "↻¹" : "↻";
            loop.ToolTip = _repeatMode == 0 ? "Loop off" : _repeatMode == 1 ? "Loop current media" : "Loop playlist";
            loop.Foreground = _repeatMode == 0 ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(184, 184, 184));
            Find<Button>("ShuffleButton").Foreground = _shuffle ? new SolidColorBrush(Color.FromRgb(184, 184, 184)) : Brushes.Gray;
            Find<Button>("ShuffleButton").ToolTip = _shuffle ? "Shuffle on" : "Shuffle off";
            Find<Button>("FavoriteButton").Content = _current != null && _favorites.Contains(_current.Path) ? "★" : "☆";
            Find<Button>("FavoriteButton").IsEnabled = _current != null;
        }

        private void ToggleFavorite(string path)
        {
            bool removing = _favorites.Contains(path);
            if (removing) _favorites.Remove(path); else _favorites.Add(path);
            if (!FavoritesStore.Save(_favorites))
            {
                if (removing) _favorites.Add(path); else _favorites.Remove(path);
                Status("Unable to save favorites"); return;
            }
            UpdateModes();
            if (_showingFavorites) ShowFavorites();
        }

        private void ShowFavorites()
        {
            _browserVersion++; _browserFolder = null; _showingFavorites = true;
            var rows = new List<SidebarEntry>();
            foreach (string path in _favorites)
            {
                if (Directory.Exists(path)) rows.Add(new SidebarEntry(path));
                else
                {
                    int skipped;
                    foreach (MediaItem media in MediaFiles.FromPaths(new string[] { path }, out skipped)) rows.Add(new SidebarEntry(media));
                }
            }
            SetSidebarRows(rows, "Favorites");
        }

        private void SetSidebarRows(IEnumerable<SidebarEntry> rows, string title)
        {
            _libraryRows = rows.ToList();
            ApplyLibraryView(); UpdateBreadcrumbs();
            Find<TextBlock>("SidebarTitle").Text = title;
            Find<TextBlock>("SidebarTitle").ToolTip = _browserFolder;
            Find<TextBlock>("CountLabel").Text = _sidebarEntries.Count.ToString();
            Find<StackPanel>("PlaylistEmpty").Visibility = _sidebarEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("UpFolderButton").IsEnabled = _browserFolder != null && Directory.GetParent(_browserFolder) != null;
            SelectCurrentRow();
        }

        private void RefreshSidebar()
        {
            if (_browserFolder == null && !_showingFavorites) SetSidebarRows(_items.Select(m => new SidebarEntry(m)), "Playlist");
        }

        private void SelectCurrentRow()
        {
            _selecting = true;
            SidebarEntry row = _current == null ? null : _sidebarEntries.FirstOrDefault(r => !r.IsFolder && String.Equals(r.Path, _current.Path, StringComparison.OrdinalIgnoreCase));
            _playlist.SelectedItem = row;
            if (row != null) _playlist.ScrollIntoView(row);
            _selecting = false;
        }

        private void SidebarSelection()
        {
            if (_selecting) return;
            SidebarEntry row = _playlist.SelectedItem as SidebarEntry;
            if (row == null) return;
            if (row.IsFolder) { BrowseSidebar(row.Path, false); return; }
            _items.Clear();
            foreach (SidebarEntry entry in _libraryRows) if (entry.Media != null) _items.Add(entry.Media);
            _shuffleHistory.Clear(); _shuffleVisited.Clear();
            OpenMedia(row.Media, true);
            UpdatePlaylist();
        }

        private void BrowseSidebar(string folder, bool playFirst)
        {
            if (!MediaFiles.IsLocalPath(folder)) return;
            int version = ++_browserVersion;
            Status("Opening folder…");
            Task.Run(delegate
            {
                var rows = new List<SidebarEntry>();
                try
                {
                    foreach (string child in Directory.EnumerateDirectories(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        if (MediaFiles.IsLocalPath(child)) rows.Add(new SidebarEntry(child));
                    int skipped;
                    foreach (MediaItem media in MediaFiles.FromFolder(folder, out skipped)) rows.Add(new SidebarEntry(media));
                }
                catch (IOException) { rows = null; }
                catch (UnauthorizedAccessException) { rows = null; }
                return rows;
            }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_libraryClosed || version != _browserVersion) return;
                if (task.IsFaulted || task.Result == null) { Status("Unable to open folder"); return; }
                _browserFolder = folder; _showingFavorites = false;
                SetSidebarRows(task.Result, new DirectoryInfo(folder).Name);
                if (playFirst)
                {
                    _items.Clear();
                    foreach (SidebarEntry row in _libraryRows) if (row.Media != null) _items.Add(row.Media);
                    _shuffleHistory.Clear(); _shuffleVisited.Clear();
                    if (_items.Count > 0) { RecentStore.Record(folder, _items.Count); OpenMedia(_items[0], true); }
                }
                UpdateControls(); Status("");
            })));
        }

        private bool NavigateShuffled(int direction, bool forcePlay)
        {
            if (!_shuffle || _items.Count < 2) return false;
            MediaItem next;
            if (direction < 0 && _shuffleHistory.Count > 0)
            {
                next = _shuffleHistory[_shuffleHistory.Count - 1];
                _shuffleHistory.RemoveAt(_shuffleHistory.Count - 1);
            }
            else
            {
                if (_current != null) _shuffleVisited.Add(_current);
                var available = _items.Where(m => m != _current && !_shuffleVisited.Contains(m)).ToList();
                if (available.Count == 0) { _shuffleVisited.Clear(); available = _items.Where(m => m != _current).ToList(); }
                next = available[_random.Next(available.Count)];
                if (_current != null) _shuffleHistory.Add(_current);
                if (_shuffleHistory.Count > 256) _shuffleHistory.RemoveAt(0);
            }
            OpenMedia(next, forcePlay || _playing || _failed);
            return true;
        }
    }
}

