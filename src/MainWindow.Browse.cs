using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private List<SidebarEntry> _libraryRows = new List<SidebarEntry>();
        private readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private static readonly IComparer<string> NameOrder = Comparer<string>.Create(MediaFiles.MediaItemComparer.CompareNatural);

        private void SetupBrowsing()
        {
            Find<TextBox>("PlaylistSearch").TextChanged += delegate { _searchDelay.Stop(); _searchDelay.Start(); };
            _searchDelay.Tick += delegate { _searchDelay.Stop(); ApplyLibraryView(); };
            Click("ClearSearchButton", delegate { Find<TextBox>("PlaylistSearch").Clear(); });
            Click("SortButton", delegate
            {
                var menu = new ContextMenu();
                string[] choices = { "Name A–Z", "Name Z–A", "Date modified · newest", "Type" };
                for (int i = 0; i < choices.Length; i++)
                {
                    int choice = i;
                    var item = new MenuItem { Header = choices[i], IsCheckable = true, IsChecked = _settings.PlaylistSort == i };
                    item.Click += delegate { _settings.PlaylistSort = choice; ApplyLibraryView(); QueuePreferences(); };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = Find<Button>("SortButton"); menu.IsOpen = true;
            });
            Find<ScrollViewer>("BreadcrumbScroll").PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e)
            {
                var scroll = (ScrollViewer)sender;
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            };
            Closed += delegate { _searchDelay.Stop(); _savePreferences.Stop(); };
        }

        private void ApplyLibraryView()
        {
            StopHover();
            string query = Find<TextBox>("PlaylistSearch").Text.Trim();

            var groups = _libraryRows.OrderBy(r => r.IsFolder ? 0 : 1);
            IEnumerable<SidebarEntry> ordered;
            switch (_settings.PlaylistSort)
            {
                case 1: ordered = groups.ThenByDescending(r => r.Name, NameOrder); break;
                case 2: ordered = groups.ThenByDescending(r => r.Modified).ThenBy(r => r.Name, NameOrder); break;
                case 3: ordered = groups.ThenBy(r => r.Kind).ThenBy(r => r.Name, NameOrder); break;
                default: ordered = groups.ThenBy(r => r.Name, NameOrder); break;
            }
            _libraryRows = ordered.ToList();
            var media = _libraryRows.Where(r => r.Media != null).Select(r => r.Media).ToList();
            var queued = new HashSet<MediaItem>(_items);
            if (queued.Count == media.Count && media.All(queued.Contains))
            {
                _items.Clear();
                foreach (MediaItem item in media) _items.Add(item);
            }
            _selecting = true;
            _sidebarEntries.Clear();
            foreach (SidebarEntry row in _libraryRows.Where(r => r.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) _sidebarEntries.Add(row);
            _selecting = false;
            Find<TextBlock>("CountLabel").Text = _sidebarEntries.Count.ToString();
            Find<StackPanel>("PlaylistEmpty").Visibility = _sidebarEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<TextBlock>("PlaylistEmptyLabel").Text = query.Length > 0 ? "No matches" : "No files yet";
            Find<Button>("ClearSearchButton").Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectCurrentRow();
        }

        private void UpdateBreadcrumbs()
        {
            StackPanel host = Find<StackPanel>("Breadcrumbs");
            host.Children.Clear();
            Find<ScrollViewer>("BreadcrumbScroll").Visibility = _browserFolder == null ? Visibility.Collapsed : Visibility.Visible;
            if (_browserFolder == null) return;
            var chain = new List<DirectoryInfo>();
            for (var directory = new DirectoryInfo(_browserFolder); directory != null; directory = directory.Parent) chain.Add(directory);
            chain.Reverse();
            foreach (DirectoryInfo directory in chain)
            {
                string target = directory.FullName;
                if (host.Children.Count > 0) host.Children.Add(new TextBlock { Text = "›", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Content = directory.Name, ToolTip = target, Padding = new Thickness(5, 3, 5, 3), Background = Brushes.Transparent, FontSize = 11, MaxWidth = 160 };
                button.Click += delegate { BrowseSidebar(target, false); };
                host.Children.Add(button);
            }
            Dispatcher.BeginInvoke(new Action(delegate { Find<ScrollViewer>("BreadcrumbScroll").ScrollToRightEnd(); }));
        }
    }
}
