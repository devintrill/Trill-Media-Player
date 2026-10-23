using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Markup;

namespace Trill
{
    // Deliberately uses ordinary filesystem reads instead of the Windows shell.
    // Previews are decoded in process (see Thumbnails); the shell thumbnail cache
    // and the shell recent-items list are never touched.
    public sealed class LocalPickerWindow : Window
    {
        private readonly bool selectFolder;
        private readonly Brush textBrush = ColorBrush("#F0F0F0");
        private readonly Brush mutedBrush = ColorBrush("#989898");
        private readonly Brush accentBrush = ColorBrush("#B8B8B8");
        private readonly Brush controlBrush = ColorBrush("#1FFFFFFF");
        private TextBox address;
        private ComboBox drives;
        private ListBox entries;
        private ListBox recents;
        private CheckBox mediaOnly;
        private TextBlock status;
        private TextBlock recentsEmpty;
        private Button open;
        private string currentFolder;
        private bool changingDrive;
        private bool changingRecent;
        private int navigationVersion;
        private CancellationTokenSource navigationCancellation;
        private bool closed;

        public string[] SelectedPaths { get; private set; }

        // Set when the user asks for the Explorer dialog instead; the caller opens it.
        public bool SwitchToWindowsDialog { get; private set; }

        public LocalPickerWindow(bool selectFolder)
        {
            this.selectFolder = selectFolder;
            SelectedPaths = new string[0];
            Title = selectFolder ? "Open folder" : "Open files";
            Width = 1080;
            Height = 720;
            MinWidth = 760;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = ColorBrush("#121212");
            Foreground = textBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            using (Stream styles = typeof(LocalPickerWindow).Assembly.GetManifestResourceStream("Trill.ControlStyles.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(styles));
            Content = BuildContent();
            Loaded += delegate
            {
                if (!SystemParameters.ClientAreaAnimation) return;
                UIElement surface = (UIElement)Content;
                TranslateTransform motion = new TranslateTransform(0, 6);
                surface.RenderTransform = motion;
                Duration duration = new Duration(TimeSpan.FromMilliseconds(180));
                surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
                motion.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            };
            Closed += delegate { closed = true; if (navigationCancellation != null) navigationCancellation.Cancel(); Thumbnails.CancelPending(); };
            Glass.Attach(this, null);
            PreviewKeyDown += OnKeyDown;
            PopulateDrives();
            LoadRecents();

            string startingFolder = FirstAvailableRecent();
            if (startingFolder == null) startingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!MediaFiles.IsLocalPath(startingFolder)) startingFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!MediaFiles.IsLocalPath(startingFolder) && drives.Items.Count > 0) startingFolder = (string)drives.Items[0];
            Navigate(startingFolder);
        }

        private UIElement BuildContent()
        {
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock hint = new TextBlock
            {
                Text = selectFolder
                    ? "Double-click a folder to go inside it. Open this folder loads what you are viewing."
                    : "Double-click a folder to go inside it. Ctrl or Shift to select several files.",
                Foreground = mutedBrush,
                FontSize = 12,
                Margin = new Thickness(2, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            layout.Children.Add(hint);

            Grid navigation = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button up = MakeButton("Up", 50);
            up.Margin = new Thickness(0, 0, 8, 0);
            up.ToolTip = "Go to the parent folder (Alt + Up or Backspace)";
            up.Click += delegate { NavigateUp(); };
            navigation.Children.Add(up);

            drives = new ComboBox { Width = 82, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6), ToolTip = "Drives", Foreground = textBrush, Background = controlBrush, Style = DriveStyle() };
            drives.SelectionChanged += delegate
            {
                if (!changingDrive && drives.SelectedItem != null) Navigate((string)drives.SelectedItem);
            };
            Grid.SetColumn(drives, 1);
            navigation.Children.Add(drives);

            address = new TextBox
            {
                Background = controlBrush, Foreground = textBrush, CaretBrush = textBrush,
                BorderBrush = ColorBrush("#2EFFFFFF"), Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Enter an existing folder on a local drive"
            };
            address.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { Navigate(address.Text); e.Handled = true; }
            };
            Grid.SetColumn(address, 2);
            navigation.Children.Add(address);
            Button go = MakeButton("Go", 50);
            go.Margin = new Thickness(8, 0, 0, 0);
            go.Click += delegate { Navigate(address.Text); };
            Grid.SetColumn(go, 3);
            navigation.Children.Add(go);

            mediaOnly = new CheckBox
            {
                Content = "Media only",
                Foreground = mutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 2, 0),
                FontSize = 12,
                ToolTip = "Hide unsupported files"
            };
            mediaOnly.Checked += delegate { Refresh(); };
            mediaOnly.Unchecked += delegate { Refresh(); };
            Grid.SetColumn(mediaOnly, 4);
            navigation.Children.Add(mediaOnly);
            Grid.SetRow(navigation, 1);
            layout.Children.Add(navigation);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(226) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(BuildRecentsRail());

            entries = new ListBox
            {
                Background = ColorBrush("#3D121212"), Foreground = textBrush, BorderBrush = ColorBrush("#1FFFFFFF"),
                BorderThickness = new Thickness(1), Padding = new Thickness(8),
                SelectionMode = selectFolder ? SelectionMode.Single : SelectionMode.Extended,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            entries.ItemTemplate = TileTemplate();
            entries.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel)));
            ScrollViewer.SetHorizontalScrollBarVisibility(entries, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(entries, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(entries, true);
            VirtualizingPanel.SetIsVirtualizing(entries, true);
            Style tileStyle = new Style(typeof(ListBoxItem));
            tileStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
            tileStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            tileStyle.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding("IsSelectable")));
            tileStyle.Setters.Add(new Setter(Control.TemplateProperty, TileContainerTemplate()));
            entries.ItemContainerStyle = tileStyle;
            entries.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e)
            {
                ListBoxItem clicked = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (clicked != null) ActivateEntry();
            };
            entries.SelectionChanged += delegate { UpdateOpenButton(); };
            Grid.SetColumn(entries, 1);
            body.Children.Add(entries);
            Grid.SetRow(body, 2);
            layout.Children.Add(body);

            status = new TextBlock { Foreground = mutedBrush, FontSize = 12, MinHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
            Grid.SetRow(status, 3);
            layout.Children.Add(status);

            DockPanel footer = new DockPanel();
            Button explorer = MakeButton("Windows Explorer…", 168);
            explorer.HorizontalAlignment = HorizontalAlignment.Left;
            explorer.ToolTip = "Open the standard Windows file dialog instead";
            explorer.Click += delegate
            {
                SwitchToWindowsDialog = true;
                DialogResult = false;
            };
            DockPanel.SetDock(explorer, Dock.Left);
            footer.Children.Add(explorer);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = MakeButton("Cancel", 88);
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            buttons.Children.Add(cancel);
            open = MakeButton(selectFolder ? "Open folder" : "Open", 132);
            open.IsEnabled = false;
            open.Background = accentBrush;
            open.Foreground = ColorBrush("#121212");
            open.BorderBrush = accentBrush;
            open.Margin = new Thickness(10, 0, 0, 0);
            open.Click += delegate { ConfirmSelection(); };
            buttons.Children.Add(open);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);

            Grid.SetRow(footer, 4);
            layout.Children.Add(footer);
            // The scrim rides on the content so the window itself can stay transparent
            // and let the compositor's frosted backdrop show through.
            return new Border { Background = ColorBrush("#59101010"), Padding = new Thickness(24), Child = layout };
        }

        private UIElement BuildRecentsRail()
        {
            Border rail = new Border
            {
                Background = ColorBrush("#3D121212"),
                BorderBrush = ColorBrush("#1FFFFFFF"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(12, 14, 12, 12)
            };
            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            content.Children.Add(new TextBlock
            {
                Text = "Recently played",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorBrush("#ACACAC"),
                Margin = new Thickness(2, 0, 0, 10)
            });

            Grid listArea = new Grid();
            recents = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = textBrush,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            recents.ItemTemplate = RecentTemplate();
            Style recentStyle = new Style(typeof(ListBoxItem));
            recentStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
            recentStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            recentStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            recentStyle.Setters.Add(new Setter(Control.TemplateProperty, RowTemplate()));
            recents.ItemContainerStyle = recentStyle;
            recents.SelectionChanged += delegate
            {
                RecentFolder chosen = recents.SelectedItem as RecentFolder;
                if (changingRecent || chosen == null) return;
                if (!MediaFiles.IsLocalPath(chosen.Path) || !Directory.Exists(chosen.Path))
                {
                    ShowError("That folder is no longer available. Choose Forget to remove it.");
                    return;
                }
                Navigate(chosen.Path);
            };
            recents.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e)
            {
                ListBoxItem clicked = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                RecentFolder chosen = recents.SelectedItem as RecentFolder;
                if (clicked == null || chosen == null) return;
                if (!selectFolder) return;
                if (!MediaFiles.IsLocalPath(chosen.Path) || !Directory.Exists(chosen.Path)) return;
                SelectedPaths = new string[] { chosen.Path };
                DialogResult = true;
            };
            listArea.Children.Add(recents);
            recentsEmpty = new TextBlock
            {
                Text = "Folders you play appear here.",
                Foreground = ColorBrush("#797979"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 4, 4, 0)
            };
            listArea.Children.Add(recentsEmpty);
            Grid.SetRow(listArea, 1);
            content.Children.Add(listArea);

            StackPanel railButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            Button forget = MakeButton("Forget", 84);
            forget.FontSize = 11;
            forget.MinHeight = 28;
            forget.ToolTip = "Remove the selected folder from this list";
            forget.Click += delegate
            {
                RecentFolder chosen = recents.SelectedItem as RecentFolder;
                if (chosen == null) { ShowError("Select a recent folder first."); return; }
                RecentStore.Remove(chosen.Path);
                LoadRecents();
                Status("Removed from recently played.");
            };
            railButtons.Children.Add(forget);
            Button clear = MakeButton("Clear all", 84);
            clear.FontSize = 11;
            clear.MinHeight = 28;
            clear.Margin = new Thickness(8, 0, 0, 0);
            clear.ToolTip = "Delete the recently played file from disk";
            clear.Click += delegate
            {
                RecentStore.Clear();
                LoadRecents();
                Status("Recently played list deleted.");
            };
            railButtons.Children.Add(clear);
            Grid.SetRow(railButtons, 2);
            content.Children.Add(railButtons);

            rail.Child = content;
            return rail;
        }

        private void LoadRecents()
        {
            List<RecentFolder> saved = RecentStore.Load();
            changingRecent = true;
            recents.ItemsSource = saved;
            recents.SelectedItem = null;
            changingRecent = false;
            recentsEmpty.Visibility = saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string FirstAvailableRecent()
        {
            foreach (RecentFolder entry in RecentStore.Load())
                if (MediaFiles.IsLocalPath(entry.Path) && Directory.Exists(entry.Path)) return entry.Path;
            return null;
        }

        private DataTemplate TileTemplate()
        {
            return (DataTemplate)XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<StackPanel Width='168'>
<Border Height='104' CornerRadius='7' Background='#590F0F0F' BorderBrush='#24FFFFFF' BorderThickness='1' ClipToBounds='True'>
<Grid>
<TextBlock x:Name='Glyph' Text='{Binding Glyph}' FontFamily='Segoe MDL2 Assets' FontSize='34' Foreground='{Binding GlyphBrush}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
<Image Source='{Binding Thumb.Image}' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/>
</Grid>
</Border>
<TextBlock Text='{Binding Name}' ToolTip='{Binding Path}' TextTrimming='CharacterEllipsis' Margin='2,7,2,0' FontSize='12'/>
<TextBlock Text='{Binding Detail}' Foreground='#929292' FontSize='10' Margin='2,3,2,0' TextTrimming='CharacterEllipsis'/>
</StackPanel>
<DataTemplate.Triggers>
<DataTrigger Binding='{Binding Thumb.HasImage}' Value='True'><Setter TargetName='Glyph' Property='Visibility' Value='Collapsed'/></DataTrigger>
</DataTemplate.Triggers>
</DataTemplate>");
        }

        private DataTemplate RecentTemplate()
        {
            return (DataTemplate)XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
<StackPanel>
<TextBlock Text='{Binding Name}' ToolTip='{Binding Path}' TextTrimming='CharacterEllipsis' FontSize='12' FontWeight='SemiBold'/>
<TextBlock Text='{Binding Detail}' Foreground='#929292' FontSize='10' Margin='0,3,0,0' TextTrimming='CharacterEllipsis'/>
</StackPanel>
</DataTemplate>");
        }

        private void PopulateDrives()
        {
            changingDrive = true;
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if ((drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.CDRom) &&
                        MediaFiles.IsLocalPath(drive.Name)) drives.Items.Add(drive.Name);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            finally { changingDrive = false; }
        }

        private void Refresh()
        {
            if (!String.IsNullOrEmpty(currentFolder)) Navigate(currentFolder);
        }

        private void Navigate(string folder)
        {
            int request = ++navigationVersion;
            if (navigationCancellation != null) navigationCancellation.Cancel();
            var cancellation = new CancellationTokenSource();
            navigationCancellation = cancellation;
            bool hideOthers = mediaOnly != null && mediaOnly.IsChecked == true;
            entries.IsEnabled = false;
            open.IsEnabled = false;
            status.Foreground = mutedBrush;
            status.Text = "Loading…";
            Task.Run(delegate { return ReadFolder(folder, hideOthers, cancellation.Token); }, cancellation.Token).ContinueWith(task =>
            {
                // Always marshal results explicitly: a picker can also be constructed
                // before a WPF synchronization context has been installed.
                Exception error = task.IsFaulted ? task.Exception.GetBaseException() : null;
                if (Dispatcher.HasShutdownStarted) { cancellation.Dispose(); return; }
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate
                {
                    try
                    {
                        if (closed || request != navigationVersion || task.IsCanceled) return;
                        if (error != null)
                        {
                            ShowError("Folder unavailable or not local.");
                            return;
                        }
                        FolderContents result = task.Result;
                        Thumbnails.CancelPending();
                        currentFolder = Path.GetFullPath(folder);
                        address.Text = currentFolder;
                        changingDrive = true;
                        drives.SelectedItem = Path.GetPathRoot(currentFolder);
                        changingDrive = false;
                        changingRecent = true;
                        recents.SelectedItem = null;
                        changingRecent = false;
                        entries.ItemsSource = result.Items;
                        entries.Items.MoveCurrentToFirst();
                        ScrollViewer scroller = FindDescendant<ScrollViewer>(entries);
                        if (scroller != null) scroller.ScrollToTop();
                        status.Text = Describe(result.Items.Count, result.Playable, result.Skipped);
                    }
                    finally
                    {
                        if (!closed && request == navigationVersion)
                        {
                            entries.IsEnabled = true;
                            UpdateOpenButton();
                            navigationCancellation = null;
                        }
                        cancellation.Dispose();
                    }
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        private FolderContents ReadFolder(string folder, bool hideOthers, CancellationToken cancellation)
        {
            if (!MediaFiles.IsLocalPath(folder) || !Directory.Exists(folder)) throw new IOException("Folder unavailable");
            var result = new FolderContents();
            foreach (FileSystemInfo item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = item.Attributes; }
                catch (IOException) { result.Skipped++; continue; }
                catch (UnauthorizedAccessException) { result.Skipped++; continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    result.Items.Add(PickerEntry.ForFolder(item.FullName));
                    continue;
                }
                MediaKind kind;
                bool media = MediaFiles.TryGetKind(item.Name, out kind);
                if (media) result.Playable++;
                if (!media && hideOthers) continue;
                FileInfo file = item as FileInfo;
                long length = file == null ? 0 : SafeLength(file);
                result.Items.Add(PickerEntry.ForFile(item.FullName, media, kind, length, !selectFolder && media));
            }
            cancellation.ThrowIfCancellationRequested();
            result.Items.Sort(PickerEntry.Order);
            return result;
        }

        private sealed class FolderContents
        {
            public readonly List<PickerEntry> Items = new List<PickerEntry>();
            public int Skipped;
            public int Playable;
        }
        private string Describe(int shown, int playable, int skipped)
        {
            if (shown == 0) return skipped > 0 ? "Nothing here that can be opened / " + skipped + " unavailable" : "This folder is empty";
            string text = shown + (shown == 1 ? " item" : " items");
            text += playable == 0 ? " / no playable media" : " / " + playable + " playable";
            if (skipped > 0) text += " / " + skipped + " unavailable";
            return text;
        }

        private static long SafeLength(FileInfo file)
        {
            try { return file.Length; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private void NavigateUp()
        {
            if (String.IsNullOrEmpty(currentFolder)) return;
            DirectoryInfo parent = Directory.GetParent(currentFolder);
            if (parent != null) Navigate(parent.FullName);
        }

        private void ActivateEntry()
        {
            PickerEntry selected = entries.SelectedItem as PickerEntry;
            if (selected == null) return;
            if (selected.IsDirectory) Navigate(selected.Path);
            else ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (navigationCancellation != null) return;
            PickerEntry selected = entries.SelectedItem as PickerEntry;
            if (selectFolder)
            {
                string chosen = selected != null && selected.IsDirectory ? selected.Path : currentFolder;
                if (!MediaFiles.IsLocalPath(chosen) || !Directory.Exists(chosen)) { ShowError("Select an available local folder."); return; }
                SelectedPaths = new string[] { chosen };
            }
            else
            {
                if (entries.SelectedItems.Count == 1 && selected != null && selected.IsDirectory) { Navigate(selected.Path); return; }
                List<string> chosen = new List<string>();
                foreach (PickerEntry entry in entries.SelectedItems)
                    if (!entry.IsDirectory && entry.IsMedia && MediaFiles.IsLocalPath(entry.Path) && File.Exists(entry.Path)) chosen.Add(entry.Path);
                if (chosen.Count == 0) { ShowError("Select one or more available photos, videos, or audio files."); return; }
                SelectedPaths = chosen.ToArray();
            }
            DialogResult = true;
        }

        private void UpdateOpenButton()
        {
            if (open == null) return;
            PickerEntry selected = entries.SelectedItem as PickerEntry;
            open.IsEnabled = selectFolder ? currentFolder != null : entries.SelectedItems.Count > 0;
            open.Content = selectFolder ? (selected != null ? "Open selected" : "Open this folder") :
                (entries.SelectedItems.Count == 1 && selected != null && selected.IsDirectory ? "Open folder" : "Open");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) { NavigateUp(); e.Handled = true; }
            else if (key == Key.Back && !address.IsKeyboardFocusWithin) { NavigateUp(); e.Handled = true; }
            else if (key == Key.Enter && entries.IsKeyboardFocusWithin) { ActivateEntry(); e.Handled = true; }
            else if (key == Key.Escape) { DialogResult = false; e.Handled = true; }
        }

        private void Status(string message)
        {
            status.Foreground = mutedBrush;
            status.Text = message;
        }

        private void ShowError(string message)
        {
            status.Foreground = ColorBrush("#C9C9C9");
            status.Text = message;
        }

        private Button MakeButton(string label, double width)
        {
            Button button = new Button
            {
                Content = label, Width = width, MinHeight = 34, Padding = new Thickness(10, 6, 10, 6),
                Background = controlBrush, Foreground = textBrush, BorderBrush = ColorBrush("#2EFFFFFF"),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 12
            };
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            ControlTemplate template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "ButtonBorder"));
            template.Triggers.Add(hover);
            Trigger focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "ButtonBorder"));
            template.Triggers.Add(focused);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "ButtonBorder"));
            template.Triggers.Add(disabled);
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78, "ButtonBorder"));
            template.Triggers.Add(pressed);
            button.Template = template;
            ScaleTransform scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.MouseEnter += delegate { AnimateScale(scale, 1.025); };
            button.MouseLeave += delegate { AnimateScale(scale, 1.0); };
            return button;
        }

        private static void AnimateScale(ScaleTransform scale, double target)
        {
            DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(110)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private static ControlTemplate TileContainerTemplate()
        {
            return (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
<Border Name='Tile' CornerRadius='9' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' Padding='{TemplateBinding Padding}' Margin='3'>
<ContentPresenter />
</Border>
<ControlTemplate.Triggers>
<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Tile' Property='Background' Value='#1FFFFFFF'/></Trigger>
<Trigger Property='IsSelected' Value='True'><Setter TargetName='Tile' Property='Background' Value='#33B8B8B8'/><Setter TargetName='Tile' Property='BorderBrush' Value='#B8B8B8'/><Setter Property='Foreground' Value='#E1E1E1'/></Trigger>
<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Tile' Property='Opacity' Value='0.42'/></Trigger>
</ControlTemplate.Triggers>
</ControlTemplate>");
        }

        private static ControlTemplate RowTemplate()
        {
            return (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
<Border Name='Row' CornerRadius='7' Background='Transparent' Padding='{TemplateBinding Padding}' Margin='0,1'>
<ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' />
</Border>
<ControlTemplate.Triggers>
<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='#1AFFFFFF'/></Trigger>
<Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='#33B8B8B8'/><Setter Property='Foreground' Value='#E1E1E1'/></Trigger>
</ControlTemplate.Triggers>
</ControlTemplate>");
        }

        private static Style DriveStyle()
        {
            return (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBox'>
<Setter Property='ItemContainerStyle'><Setter.Value><Style TargetType='ComboBoxItem'>
<Setter Property='Foreground' Value='#F0F0F0'/><Setter Property='Padding' Value='10,7'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border Name='Item' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#3E3E3E'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style></Setter.Value></Setter>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
<Grid>
<ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Name='Toggle' CornerRadius='8' Background='#2A2A2A' BorderBrush='#3A3A3A' BorderThickness='1'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Toggle' Property='BorderBrush' Value='#B8B8B8'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
</ToggleButton>
<Grid IsHitTestVisible='False' Margin='10,0'><ContentPresenter Content='{TemplateBinding SelectionBoxItem}' VerticalAlignment='Center' Margin='0,0,17,0'/><TextBlock Text='⌄' Foreground='#989898' HorizontalAlignment='Right' VerticalAlignment='Center'/></Grid>
<Popup Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
<Border Background='#2A2A2A' BorderBrush='#3A3A3A' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,4,0,0' MinWidth='82'><ScrollViewer MaxHeight='260'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border>
</Popup>
</Grid>
</ControlTemplate></Setter.Value></Setter>
</Style>");
        }

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null)
            {
                T result = element as T;
                if (result != null) return result;
                FrameworkContentElement content = element as FrameworkContentElement;
                element = content != null ? content.Parent : VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private static T FindDescendant<T>(DependencyObject element) where T : DependencyObject
        {
            if (element == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, index);
                T match = child as T;
                if (match != null) return match;
                match = FindDescendant<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        private static Brush ColorBrush(string color)
        {
            Brush result = (Brush)new BrushConverter().ConvertFromString(color);
            result.Freeze();
            return result;
        }

        public sealed class PickerEntry
        {
            public string Path { get; private set; }
            public string Name { get; private set; }
            public bool IsDirectory { get; private set; }
            public bool IsMedia { get; private set; }
            public bool IsSelectable { get; private set; }
            public string Glyph { get; private set; }
            public Brush GlyphBrush { get; private set; }
            public string Detail { get; private set; }
            public ThumbnailHandle Thumb { get; private set; }

            private static readonly Brush FolderGlyph = ColorBrush("#B8B8B8");
            private static readonly Brush MediaGlyph = ColorBrush("#AEAEAE");
            private static readonly Brush OtherGlyph = ColorBrush("#676767");

            private PickerEntry(string path)
            {
                Path = path;
                Name = System.IO.Path.GetFileName(path);
                if (String.IsNullOrEmpty(Name)) Name = path;
            }

            internal static PickerEntry ForFolder(string path)
            {
                PickerEntry entry = new PickerEntry(path);
                entry.IsDirectory = true;
                entry.IsSelectable = true;
                entry.Glyph = "";
                entry.GlyphBrush = FolderGlyph;
                entry.Detail = "Folder";
                return entry;
            }

            internal static PickerEntry ForFile(string path, bool media, MediaKind kind, long length, bool selectable)
            {
                PickerEntry entry = new PickerEntry(path);
                entry.IsMedia = media;
                entry.IsSelectable = selectable;
                string extension = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                if (!media)
                {
                    entry.Glyph = "";
                    entry.GlyphBrush = OtherGlyph;
                    entry.Detail = Join(extension.Length == 0 ? "File" : extension + " file", Size(length));
                    return entry;
                }
                switch (kind)
                {
                    case MediaKind.Video: entry.Glyph = ""; break;
                    case MediaKind.Audio: entry.Glyph = ""; break;
                    default: entry.Glyph = ""; break;
                }
                entry.GlyphBrush = MediaGlyph;
                entry.Detail = Join(extension, Size(length));
                if (kind != MediaKind.Audio) entry.Thumb = new ThumbnailHandle(path, kind);
                return entry;
            }

            private static string Join(string left, string right)
            {
                if (String.IsNullOrEmpty(right)) return left;
                return left + "  ·  " + right;
            }

            private static string Size(long bytes)
            {
                if (bytes <= 0) return "";
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double value = bytes;
                int unit = 0;
                while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
                return value.ToString(value >= 10 || unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
            }

            internal static readonly Comparison<PickerEntry> Order = delegate(PickerEntry a, PickerEntry b)
            {
                if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
                if (!a.IsDirectory && a.IsMedia != b.IsMedia) return a.IsMedia ? -1 : 1;
                return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            };
        }
    }
}
