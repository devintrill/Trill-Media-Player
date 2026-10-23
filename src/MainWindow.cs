using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow : Window
    {
        private readonly Grid _root;
        private readonly ObservableCollection<MediaItem> _items = new ObservableCollection<MediaItem>();
        private MediaElement _video;
        private readonly Image _photo;
        private readonly ListBox _playlist;
        private readonly Slider _seek;
        private readonly Slider _volume;
        private readonly DispatcherTimer _timer;
        private readonly PhotoLoader _photoLoader;
        private readonly AudioSpectrum _audioSpectrum;
        private readonly SpectrumView _spectrum = new SpectrumView { Height = 180, MinWidth = 300, MaxWidth = 480, Margin = new Thickness(0, 12, 0, 0) };
        private readonly ScrubPreview _scrubPreview;
        private readonly Stopwatch _scrubClock = new Stopwatch();
        private readonly DispatcherTimer _previewHandoff = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        private VideoFrame _shownScrubFrame;
        private double _lastScrubX, _fallbackTarget = -1, _fallbackAt;
        private double _sidebarWidth = 240;
        private bool _sidebarDragging;
        private bool _sidebarAnimating;
        private readonly ScaleTransform _mediaScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _mediaPan = new TranslateTransform();
        private double _zoom = 1;
        private bool _panning;
        private Point _panStart, _panOrigin;
        private readonly Stopwatch _statusClock = new Stopwatch();
        private AppSettings _settings = AppSettings.CreateDefault();
        private MediaItem _current;
        private bool _playing; // Desired transport state, preserved across still photos.
        private bool _opened;
        private bool _syncing;
        private bool _selecting;
        private bool _scrubbing;
        private bool _failed;
        private bool _fullscreen;
        private double _duration;
        private Brush _solidBackground;
        private Brush _glassScrim;
        private bool _glass;
        private WindowState _savedState;
        private Rect _savedBounds;

        public MainWindow(string[] arguments) : this(arguments, null) { }
        public MainWindow(string[] arguments, string settingsPath)
        {
            _settingsPath = settingsPath; _settings = SettingsStore.Load(settingsPath);
            _sidebarWidth = _settings.SidebarWidth;
            Title = "Trill Media Player";
            Width = Math.Min(_settings.WindowWidth, SystemParameters.WorkArea.Width); Height = Math.Min(_settings.WindowHeight, SystemParameters.WorkArea.Height); MinWidth = 940; MinHeight = 610;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trill.MainWindow.xaml"))
                _root = (Grid)XamlReader.Load(stream);
            Glass.Attach(this, delegate(bool applied) { _glass = applied; });
            using (Stream controls = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trill.ControlStyles.xaml"))
            {
                _root.Resources.Remove(typeof(Slider));
                _root.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(controls));
            }
            Content = _root;
            using (Stream logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trill.Logo.png"))
            {
                var logo = new BitmapImage();
                logo.BeginInit(); logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.DecodePixelWidth = 256; logo.StreamSource = logoStream; logo.EndInit(); logo.Freeze();
                Icon = logo;
                Find<Image>("WelcomeLogo").Source = logo;
            }
            _solidBackground = Background;
            _glassScrim = _root.Background;
            _video = Find<MediaElement>("Video");
            _photo = Find<Image>("Photo");
            _playlist = Find<ListBox>("Playlist");
            _seek = Find<Slider>("SeekSlider");
            _volume = Find<Slider>("VolumeSlider");
            SetupLibrary();
            _audioSpectrum = new AudioSpectrum(Dispatcher, delegate(double[] values, float[] wave) { _spectrum.Update(values, wave); });
            _photoLoader = new PhotoLoader(Dispatcher);
            // 960 rather than the default: smaller frames let far more of them sit in
            // the same cache, which is what keeps the decoder ahead of a drag.
            _scrubPreview = new ScrubPreview(Dispatcher, delegate { RenderScrub(null, EventArgs.Empty); }, ScrubPreview.CacheLimit, 960);

            Click("AddFilesButton", delegate { ChooseFiles(); });
            Click("WelcomeOpenButton", delegate { ChooseFiles(); });
            Click("FolderButton", delegate { ChooseFolder(); });
            Click("WelcomeFolderButton", delegate { ChooseFolder(); });
            Click("ZoomResetButton", delegate { ResetMediaView(); });
            Click("SettingsButton", delegate { ShowSettings(); });
            Click("ClearButton", delegate { ClearPlaylist(); });
            Click("PlayButton", delegate { TogglePlay(); });
            Click("PreviousButton", delegate { Navigate(-1, false); });
            Click("NextButton", delegate { Navigate(1, false); });
            Click("BackButton", delegate { SeekBy(-10); });
            Click("ForwardButton", delegate { SeekBy(10); });
            Click("MuteButton", delegate { ToggleMute(); });
            Click("FullscreenButton", delegate { ToggleFullscreen(); });
            Click("AbLoopButton", delegate { CycleAbLoop(); });
            _seek.SizeChanged += delegate { LayoutAbMarks(); };
            UpdateAbLoop();
            Click("ExportButton", delegate { ExportFrame(); });
            Click("PlaylistButton", delegate { TogglePlaylist(); });
            Click("SpeedButton", delegate { ShowSpeedMenu(); });
            _playlist.SelectionChanged += delegate { SidebarSelection(); };
            _playlist.MouseDoubleClick += delegate { if (_current != null && !_playing) TogglePlay(); };
            ConnectPlayer(_video); SetupPreloading(); SetupHover(); SetupLens(); SetupFocus();
            _seek.ValueChanged += SeekChanged;
            BuildSpectrum();
            Closed += delegate { Thumbnails.Shutdown(); _audioSpectrum.Dispose(); };
            _previewHandoff.Tick += delegate { HideScrubPreview(); };
            _seek.PreviewMouseLeftButtonDown += ScrubPointerDown;
            _seek.PreviewMouseMove += ScrubPointerMove;
            _seek.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (_scrubbing) { EndScrub(); e.Handled = true; } };
            _seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate { BeginScrub(); }), true);
            _seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate { EndScrub(); }), true);
            _seek.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler(delegate
            {
                // Capture can move from the track to its thumb without ending a drag.
                if (!_seek.IsMouseCaptureWithin) EndScrub();
            }), true);
            Deactivated += delegate { EndScrub(); };
            _volume.ValueChanged += delegate
            {
                if (_syncing) return;
                _settings.Volume = _volume.Value;
                if (_settings.Volume > 0) _settings.IsMuted = false;
                ApplyAudio(); QueuePreferences();
            };
            Grid stage = Find<Grid>("Stage");
            var viewTransform = new TransformGroup();
            viewTransform.Children.Add(_mediaScale);
            viewTransform.Children.Add(_mediaPan);
            Find<Grid>("VisualMedia").RenderTransform = viewTransform;
            stage.PreviewMouseWheel += MediaWheel;
            stage.MouseLeftButtonDown += MediaPointerDown;
            stage.MouseMove += MediaPointerMove;
            stage.MouseLeftButtonUp += delegate { EndMediaPan(); };
            stage.LostMouseCapture += delegate { _panning = false; };
            stage.SizeChanged += delegate { SetMediaView(_zoom, _mediaPan.X, _mediaPan.Y, false); };
            GridSplitter splitter = Find<GridSplitter>("SidebarSplitter");
            splitter.DragStarted += delegate
            {
                ColumnDefinition column = Find<Grid>("BodyGrid").ColumnDefinitions[0];
                double width = column.ActualWidth;
                _sidebarAnimating = false;
                _sidebarDragging = true;
                column.BeginAnimation(ColumnDefinition.WidthProperty, null);
                column.MinWidth = 0;
                column.Width = new GridLength(width);
            };
            splitter.DragCompleted += delegate { FinishSidebarDrag(); };
            splitter.KeyUp += delegate { FinishSidebarDrag(); };
            splitter.MouseDoubleClick += delegate { _sidebarWidth = 240; SetPlaylistVisible(); };
            Find<Grid>("BodyGrid").SizeChanged += delegate { UpdateSidebarBounds(); };
            PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                Point point = e.GetPosition(this);
                if (_focusMode) FocusPointer(e.GetPosition(_root));
                else if ((point - _lastFullscreenPointer).Length > 2) ShowFullscreenControls();
                _lastFullscreenPointer = point;
            };
            Deactivated += delegate { if (_fullscreen) Topmost = false; };
            Activated += delegate { if (_fullscreen) Topmost = true; };
            AllowDrop = true;
            PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += FilesDropped;
            PreviewKeyDown += HandleKey;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _timer.Tick += delegate { Tick(); };
            _timer.Start();
            Closed += delegate { CancelScrubPreview(); _scrubPreview.Dispose(); _photoLoader.Dispose(); _timer.Stop(); _video.Close(); _photo.Source = null; _items.Clear(); };
            ApplySettings();
            UpdateControls();
            _savePreferences.Tick += delegate { SavePreferences(); };
            SizeChanged += delegate { QueuePreferences(); };
            StateChanged += delegate { QueuePreferences(); };
            Closing += delegate { SavePreferences(); };
            Loaded += delegate
            {
                if (_settings.PlaylistTiles) TogglePlaylistLayout();
                if (_settings.Maximized) WindowState = WindowState.Maximized;
                _preferencesReady = true;
                if (arguments != null && arguments.Length > 0) ImportPaths(arguments);
            };
        }


        private T Find<T>(string name) where T : FrameworkElement { return (T)_root.FindName(name); }
        private void Click(string name, RoutedEventHandler handler) { Find<Button>(name).Click += handler; }
        private void Status(string message)
        {
            Find<TextBlock>("StatusLabel").Text = message;
            Find<Border>("StatusToast").Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            _statusClock.Restart();
        }
        private static string TimeText(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, Double.IsNaN(seconds) || Double.IsInfinity(seconds) ? 0 : seconds));
            return time.TotalHours >= 1 ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds)
                : String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
        }

        private string[] PickPaths(bool selectFolder)
        {
            if (_settings.UseWindowsFileDialogs)
            {
                try { return WindowsPicker.Show(this, selectFolder); }
                catch (COMException) { Status("Windows dialog unavailable"); return null; }
            }
            var picker = new LocalPickerWindow(selectFolder) { Owner = this };
            bool chosen = picker.ShowDialog() == true;
            if (!chosen && picker.SwitchToWindowsDialog)
            {
                try { return WindowsPicker.Show(this, selectFolder); }
                catch (COMException) { Status("Windows dialog unavailable"); return null; }
            }
            return chosen ? picker.SelectedPaths : null;
        }

        private void ChooseFiles()
        {
            string[] paths = PickPaths(false);
            if (paths != null && paths.Length > 0) ImportPaths(paths);
        }

        private void ChooseFolder()
        {
            string[] paths = PickPaths(true);
            if (paths == null || paths.Length == 0) return;
            BrowseSidebar(paths[0], true);
        }

        private void ImportPaths(IEnumerable<string> paths)
        {
            var incoming = new List<string>(paths);
            if (incoming.Count == 1 && Directory.Exists(incoming[0])) { BrowseSidebar(incoming[0], true); return; }
            _browserVersion++; _browserFolder = null; _showingFavorites = false;
            var batch = new List<MediaItem>();
            var direct = new List<string>();
            int skippedTotal = 0;
            foreach (string path in paths)
            {
                if (!MediaFiles.IsLocalPath(path)) { skippedTotal++; continue; }
                if (Directory.Exists(path))
                {
                    int skipped;
                    List<MediaItem> found = MediaFiles.FromFolder(path, out skipped);
                    batch.AddRange(found);
                    skippedTotal += skipped;
                    if (found.Count > 0) RecentStore.Record(path, found.Count);
                }
                else direct.Add(path);
            }
            int rejected;
            batch.AddRange(MediaFiles.FromPaths(direct, out rejected));
            AddItems(batch, skippedTotal + rejected);
        }

        private void AddItems(IEnumerable<MediaItem> files, int skipped)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MediaItem item in _items) known.Add(item.Path);
            int added = 0;
            foreach (MediaItem item in files)
            {
                if (!known.Add(item.Path)) continue;
                _items.Add(item); added++;
            }
            UpdatePlaylist();
            if (_current == null && _items.Count > 0) OpenMedia(_items[0], true);
            Status(added > 0 ? String.Format("Added {0} item{1}. {2}", added, added == 1 ? "" : "s", skipped > 0 ? skipped + " skipped" : "")
                : skipped > 0 ? "No supported local files" : "Already in playlist");
        }

        private void FilesDropped(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) ImportPaths(paths);
            e.Handled = true;
        }

        private void UpdatePlaylist()
        {
            RefreshSidebar();
            Find<TextBlock>("CountLabel").Text = _sidebarEntries.Count.ToString();
            Find<StackPanel>("PlaylistEmpty").Visibility = _sidebarEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Find<Button>("ClearButton").IsEnabled = _items.Count > 0;
            UpdateControls();
        }

        private void ClearPlaylist()
        {
            ClearWarmMedia(); ClearSwitchCover(); StopHover();
            _browserVersion++; _browserFolder = null; _showingFavorites = false;
            _shuffleVisited.Clear(); _shuffleHistory.Clear();
            CancelScrubPreview();
            ResetMediaView();
            _photoLoader.Cancel();
            _scrubbing = false;
            _playing = false; _opened = false; _failed = false; _duration = 0;
            _current = null;
            _video.Close(); _video.Source = null; _video.Visibility = Visibility.Collapsed;
            _photo.Source = null; _photo.Visibility = Visibility.Collapsed;
            Find<StackPanel>("AudioPanel").Visibility = Visibility.Collapsed;
            StopSpectrum();
            _items.Clear();
            ShowWelcome("Trill Media Player", "Drop files here", false);
            Title = "Trill Media Player";

            UpdatePlaylist(); UpdateTimeline(0, 0);
            Status("Playlist cleared");
        }

        private void OpenMedia(MediaItem item, bool play)
        {
            HoldSwitchFrame(item); StopHover();
            WarmMedia warmed = TakeWarm(item);
            CancelScrubPreview();
            ResetMediaView();
            _photoLoader.Cancel();
            _opened = false; _failed = false; _duration = 0; _scrubbing = false;
            ClearAbLoop(false);
            _current = item; _playing = play;
            _video.Close(); _video.Source = null; _video.Visibility = Visibility.Collapsed;
            if (warmed != null && warmed.Player != null)
            {
                Find<Grid>("VisualMedia").Children.Remove(_video);
                _video = warmed.Player; _video.Opacity = 1;
                ConnectPlayer(_video);
            }
            _photo.Source = null; _photo.Visibility = Visibility.Collapsed;
            Find<StackPanel>("AudioPanel").Visibility = Visibility.Collapsed;
            StopSpectrum();
            SelectCurrentRow();
            Find<StackPanel>("WelcomePanel").Visibility = Visibility.Collapsed;
            Title = item.Name + " · Trill Media Player";
            Find<Border>("StageBorder").Background = item.IsAudio ? (Brush)_root.Resources["EmptyStageBrush"] : Brushes.Black;
            UpdateTimeline(0, 0);
            if (!MediaFiles.IsLocalPath(item.Path) || !File.Exists(item.Path)) { MediaFailed(); return; }
            try
            {
                if (item.HasTimeline)
                {
                    _video.Visibility = Visibility.Visible;
                    if (item.IsAudio)
                    {
                        Find<StackPanel>("AudioPanel").Visibility = Visibility.Visible;
                        StartSpectrum(item);
                        Find<TextBlock>("AudioTitle").Text = item.Name;
                        Find<TextBlock>("AudioDetail").Text = item.Detail;
                    }
                    if (warmed == null) _video.Source = new Uri(item.Path, UriKind.Absolute);
                    ApplyAudio();
                    if (play) _video.Play(); else _video.Pause();
                    Status("Opening…");
                    if (warmed != null) MediaOpened(_video, new RoutedEventArgs());
                }
                else
                {
                    Status("Opening…");
                    if (warmed != null) MediaOpened(_video, new RoutedEventArgs());
                    Action<BitmapSource> showPhoto = delegate(BitmapSource bitmap)
                    {
                        if (_current != item) return;
                        if (bitmap == null) { MediaFailed(); return; }
                        _photo.Source = bitmap;
                        _photo.Visibility = Visibility.Visible;
                        _photo.BeginAnimation(UIElement.OpacityProperty, null); _photo.Opacity = 1;
                        ClearSwitchCover(); QueueNeighbors();
                        _opened = true;
                        _duration = 0;
                        UpdateTimeline(0, _duration);
                        UpdateControls();
                        Status("");
                    };
                    if (warmed != null && warmed.Photo != null) Dispatcher.BeginInvoke(new Action(delegate { showPhoto(warmed.Photo); }));
                    else _photoLoader.Load(item.Path, showPhoto);
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is NotSupportedException)
                    && !(error is FileFormatException) && !(error is ArgumentException) && !(error is System.Runtime.InteropServices.COMException)) throw;
                MediaFailed();
            }
            UpdateControls();
        }

        private void MediaOpened(object sender, RoutedEventArgs e)
        {
            if (sender != _video || _current == null || !_current.HasTimeline || _video.Source == null || !String.Equals(_video.Source.LocalPath, _current.Path, StringComparison.OrdinalIgnoreCase)) return;
            _opened = true;
            if (_current.IsVideo) _scrubPreview.Prepare(_current.Path);
            if (_current.IsAudio) _audioSpectrum.Open(_current.Path);
            _video.BeginAnimation(UIElement.OpacityProperty, null); _video.Opacity = 1;
            _switchHandoff.Start(); QueueNeighbors();
            _duration = _video.NaturalDuration.HasTimeSpan ? _video.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _video.SpeedRatio = _settings.PlaybackSpeed;
            ApplyAudio();
            if (!_playing) _video.Pause();
            UpdateTimeline(0, _duration);
            UpdateControls();
            Status(_current.IsAudio ? "Audio  ·  " + TimeText(_duration) : _video.NaturalVideoWidth + " × " + _video.NaturalVideoHeight);
        }

        private void MediaFailed()
        {
            ClearSwitchCover(); ClearWarmMedia();
            if (_current == null) return;
            _scrubbing = false;
            CancelScrubPreview();
            ResetMediaView();
            _failed = true; _opened = false; _playing = false;
            _video.Close(); _video.Source = null; _video.Visibility = Visibility.Collapsed;
            _photo.Source = null; _photo.Visibility = Visibility.Collapsed;
            Find<StackPanel>("AudioPanel").Visibility = Visibility.Collapsed;
            StopSpectrum();
            ShowWelcome("Unable to open file", "Unsupported format or unavailable file.", true);
            Status("Choose another file to continue");
            UpdateTimeline(0, 0); UpdateControls();
        }

        private void ShowWelcome(string title, string description, bool error)
        {
            Find<Border>("StageBorder").Background = (Brush)_root.Resources["EmptyStageBrush"];
            Find<StackPanel>("WelcomePanel").Visibility = Visibility.Visible;
            Find<TextBlock>("WelcomeTitle").Text = title;
            Find<TextBlock>("WelcomeDescription").Text = description;
            Find<TextBlock>("WelcomeHint").Text = "";
        }

        private void TogglePlay()
        {
            if (_current == null) { ChooseFiles(); return; }
            if (!_current.HasTimeline) return;
            if (_failed) { OpenMedia(_current, true); return; }
            FinishFrameStep();
            _playing = !_playing;
            if (_scrubbing || !_opened) { UpdateControls(); return; }
            if (_playing)
            {
                if (_duration > 0 && _video.Position.TotalSeconds >= _duration - 0.15) _video.Position = TimeSpan.Zero;
                _video.Play();
            }
            else _video.Pause();
            UpdateControls();
            Status(_playing ? "Playing" : "Paused");
        }

        private void Navigate(int direction, bool forcePlay)
        {
            if (_items.Count == 0) return;
            if (NavigateShuffled(direction, forcePlay)) return;
            int index = _current == null ? -1 : _items.IndexOf(_current);
            int next = index + direction;
            if (next < 0 || next >= _items.Count)
            {
                if (_settings.LoopPlaylist) next = (next + _items.Count) % _items.Count;
                else
                {
                    Status(direction > 0 ? "End of playlist" : "Start of playlist");
                    return;
                }
            }
            OpenMedia(_items[next], forcePlay || _playing || _failed);
        }

        private void CompleteItem()
        {
            if (!_playing || _scrubbing || !_opened || _failed || _current == null || !_current.HasTimeline) return;
            if (_repeatMode == 0) { _playing = false; _video.Pause(); UpdateControls(); return; }
            if (_repeatMode == 2 && _items.Count > 0)
            {
                if (!NavigateShuffled(1, true)) OpenMedia(_items[(_items.IndexOf(_current) + 1) % _items.Count], true);
                return;
            }
            _video.Position = TimeSpan.Zero;
            _video.Play();
            UpdateTimeline(0, _duration);
        }
        private void ScrubPointerDown(object sender, MouseButtonEventArgs e)
        {
            BeginScrub();
            if (!_scrubbing) return;
            _lastScrubX = e.GetPosition(_seek).X;
            Track track = (Track)_seek.Template.FindName("PART_Track", _seek);
            if (track != null && !track.Thumb.IsMouseOver)
                _seek.Value = Math.Max(0, Math.Min(_duration, track.ValueFromPoint(e.GetPosition(track))));
            _seek.CaptureMouse();
            _seek.Focus();
            e.Handled = true;
        }

        private void ScrubPointerMove(object sender, MouseEventArgs e)
        {
            if (!_scrubbing || !_seek.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            double x = e.GetPosition(_seek).X;
            MoveLensPointer(x - _lastScrubX, e.GetPosition(_seek).Y, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            _lastScrubX = x;
            e.Handled = true;
        }

        private void MoveScrubPointer(double pixels, bool fine)
        {
            Track track = (Track)_seek.Template.FindName("PART_Track", _seek);
            double width = track == null ? _seek.ActualWidth - 16 : track.ActualWidth - track.Thumb.ActualWidth;
            double secondsPerPixel = fine ? _scrubPreview.FrameDuration : _duration / Math.Max(1, width);
            _seek.Value = Math.Max(0, Math.Min(_duration, _seek.Value + pixels * secondsPerPixel));
        }

        private void BeginScrub()
        {
            if (_scrubbing || !_seek.IsEnabled || _current == null || !_current.HasTimeline || !_opened) return;
            FinishFrameStep(); ClearSwitchCover(); HideScrubPreview();
            _scrubbing = true;
            _video.Pause();
            _fallbackTarget = -1; _fallbackAt = -100;
            _scrubClock.Restart(); ShowLens();
            if (_current.IsVideo)
            {
                _scrubPreview.Start(_current.Path, Math.Min(_seek.Value, Math.Max(0, _duration - 0.00001)));
                CompositionTarget.Rendering += RenderScrub;
            }
        }

        private void RenderScrub(object sender, EventArgs e)
        {
            if ((!_scrubbing && !_frameStepping) || _current == null || !_current.IsVideo) return;
            VideoFrame frame = _scrubPreview.CurrentFrame();
            if (frame != null && frame != _shownScrubFrame)
            {
                _shownScrubFrame = frame;
                Find<Image>("ScrubImage").Source = frame.Bitmap;
                Find<Border>("ScrubSurface").Visibility = Visibility.Visible;
            }
            // Formats without a Source Reader decoder keep a throttled native preview.
            if (_scrubPreview.Unavailable && _fallbackTarget != _seek.Value &&
                _scrubClock.Elapsed.TotalMilliseconds - _fallbackAt >= 80)
            {
                _fallbackTarget = _seek.Value;
                _fallbackAt = _scrubClock.Elapsed.TotalMilliseconds;
                _video.Position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(_duration, _fallbackTarget)));
            }
        }

        private void HideScrubPreview()
        {
            _previewHandoff.Stop();
            _shownScrubFrame = null;
            Find<Image>("ScrubImage").Source = null;
            Find<Border>("ScrubSurface").Visibility = Visibility.Collapsed;
        }

        private void CancelScrubPreview()
        {
            HideLens();
            _frameStepping = false;
            CompositionTarget.Rendering -= RenderScrub;
            _scrubClock.Stop();
            _scrubPreview.Cancel();
            HideScrubPreview();
        }

        private void EndScrub()
        {
            HideLens();
            if (!_scrubbing) return;
            _scrubbing = false;
            if (_seek.IsMouseCaptured) _seek.ReleaseMouseCapture();
            CompositionTarget.Rendering -= RenderScrub;
            _scrubClock.Stop();
            _scrubPreview.Stop();
            if (_current == null || !_current.HasTimeline || !_opened || _failed || _duration <= 0)
            {
                HideScrubPreview();
                return;
            }
            double target = Math.Max(0, Math.Min(_duration, _seek.Value));
            _video.Position = TimeSpan.FromSeconds(target);
            if (_playing) _video.Play(); else _video.Pause();
            // Hold only the matching frame briefly while the playback renderer catches up.
            if (_shownScrubFrame != null && _shownScrubFrame.Contains(target)) _previewHandoff.Start();
            else HideScrubPreview();
            UpdateTimeline(target, _duration);
        }
        private void SeekBy(double seconds)
        {
            FinishFrameStep();
            if (_current == null || !_current.HasTimeline || !_opened || _duration <= 0) return;
            if (_scrubbing) { _seek.Value = Math.Max(0, Math.Min(_duration, _seek.Value + seconds)); return; }
            double target = Math.Max(0, Math.Min(_duration, _video.Position.TotalSeconds + seconds));
            _video.Position = TimeSpan.FromSeconds(target);
            UpdateTimeline(target, _duration);
            Status((seconds > 0 ? "Forward " : "Back ") + Math.Abs(seconds) + " seconds");
        }

        private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing || _current == null || !_current.HasTimeline || !_opened || _duration <= 0) return;
            if (!_scrubbing) FinishFrameStep();
            if (!_scrubbing) _video.Position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(_duration, _seek.Value)));
            else if (_current.IsVideo)
            {
                _scrubPreview.Request(Math.Max(0, Math.Min(_duration - 0.00001, _seek.Value)));
                RenderScrub(null, EventArgs.Empty);
            }
            Find<TextBlock>("PositionLabel").Text = TimeText(_seek.Value); UpdateLens();
        }

        private void Tick()
        {
            TickFullscreen();
            UpdateSpectrum();
            if (_statusClock.IsRunning && _statusClock.Elapsed.TotalSeconds > 3)
            {
                Find<Border>("StatusToast").Visibility = Visibility.Collapsed;
                Find<TextBlock>("StatusLabel").Text = "";
                _statusClock.Stop();
            }
            HoldAbLoop();
            if (_current != null && _current.HasTimeline && _opened && !_failed && !_scrubbing && _playing)
                UpdateTimeline(_video.Position.TotalSeconds, _duration);
        }
        private void UpdateTimeline(double position, double duration)
        {
            _syncing = true;
            _seek.Maximum = Math.Max(1, duration);
            _seek.Value = Math.Max(0, Math.Min(_seek.Maximum, position));
            _syncing = false;
            bool timed = _current != null && _current.HasTimeline;
            string positionText = timed ? TimeText(position) : "";
            string durationText = timed ? TimeText(duration) : "";
            TextBlock positionLabel = Find<TextBlock>("PositionLabel");
            TextBlock durationLabel = Find<TextBlock>("DurationLabel");
            if (positionLabel.Text != positionText) positionLabel.Text = positionText;
            if (durationLabel.Text != durationText) durationLabel.Text = durationText;
        }

        private void UpdateControls()
        {
            UpdateZoomControl();
            UpdateModes();
            bool timed = _current != null && _current.HasTimeline;
            Find<Button>("PlayButton").IsEnabled = _current == null || timed;
            Find<Button>("SpeedButton").IsEnabled = _current == null || timed;
            Find<TextBlock>("PlayGlyph").Text = timed && _playing ? "\uE769" : "\uE768";
            Find<Button>("PlayButton").ToolTip = timed && _playing ? "Pause" : "Play";
            int index = _current == null ? -1 : _items.IndexOf(_current);
            Find<Button>("PreviousButton").IsEnabled = _items.Count > 0 && (index > 0 || _settings.LoopPlaylist || _shuffle);
            Find<Button>("NextButton").IsEnabled = _items.Count > 0 && (index < _items.Count - 1 || _settings.LoopPlaylist || _shuffle);
            bool canSeek = _current != null && _current.HasTimeline && _opened && _duration > 0 && !_failed;
            _seek.IsEnabled = canSeek;
            Find<Button>("BackButton").IsEnabled = canSeek;
            Find<Button>("ForwardButton").IsEnabled = canSeek;
        }

        private static string SpeedText(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture) + "×"; }
        private void SetSpeed(double value)
        {
            _settings.PlaybackSpeed = Math.Max(0.25, Math.Min(4, value));
            _video.SpeedRatio = _settings.PlaybackSpeed;
            Find<Button>("SpeedButton").Content = SpeedText(_settings.PlaybackSpeed);
            QueuePreferences();
            Status("Video speed " + SpeedText(_settings.PlaybackSpeed) + (_current != null && !_current.HasTimeline ? " · Applies to video and audio" : ""));
        }

        private void ShowSpeedMenu()
        {
            var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), Foreground = Brushes.White };
            foreach (double speed in new double[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4 })
            {
                double selectedSpeed = speed;
                var choice = new MenuItem { Header = SpeedText(speed) + (speed == 1 ? "   Normal" : ""), IsCheckable = true, IsChecked = Math.Abs(speed - _settings.PlaybackSpeed) < .001 };
                choice.Click += delegate { SetSpeed(selectedSpeed); };
                menu.Items.Add(choice);
            }
            menu.PlacementTarget = Find<Button>("SpeedButton");
            menu.IsOpen = true;
        }

        private void ApplyAudio()
        {
            _video.Volume = _settings.Volume;
            _video.IsMuted = _settings.IsMuted;
            Find<TextBlock>("VolumeGlyph").Text = _settings.IsMuted ? "\uE74F" : "\uE767";
            Find<Button>("MuteButton").ToolTip = _settings.IsMuted ? "Unmute" : "Mute";
            Find<TextBlock>("VolumeLabel").Text = Math.Round(_settings.Volume * 100) + "%";
        }

        private void ToggleMute() { _settings.IsMuted = !_settings.IsMuted; ApplyAudio(); QueuePreferences(); }
        private void ChangeVolume(double delta)
        {
            _volume.Value = Math.Max(0, Math.Min(1, _volume.Value + delta));
        }

        private void ApplySettings()
        {
            _syncing = true; _volume.Value = _settings.Volume; _syncing = false;
            ApplyAudio();
            _video.SpeedRatio = _settings.PlaybackSpeed;
            Find<Button>("SpeedButton").Content = SpeedText(_settings.PlaybackSpeed);
            SetPlaylistVisible();
            UpdateControls();
        }

        private void ShowSettings()
        {
            var settings = new SettingsWindow(_settings) { Owner = this };
            if (settings.ShowDialog() == true)
            {
                _settings = settings.Result;
                ApplySettings();
                SavePreferences();
            }
        }

        private void TogglePlaylist() { _settings.ShowPlaylist = !_settings.ShowPlaylist; SetPlaylistVisible(); QueuePreferences(); }
        private void RememberSidebarWidth()
        {
            if (_sidebarAnimating || _sidebarDragging || !_settings.ShowPlaylist) return;
            _sidebarWidth = Find<Grid>("BodyGrid").ColumnDefinitions[0].ActualWidth; QueuePreferences();
        }

        // Dragged narrower than this, the sidebar closes rather than leaving a stub.
        private const double SidebarSnapWidth = 130;
        private const double SidebarMinWidth = 210;

        private void FinishSidebarDrag()
        {
            _sidebarDragging = false;
            ColumnDefinition column = Find<Grid>("BodyGrid").ColumnDefinitions[0];
            // The splitter writes Width directly; ActualWidth still holds the pre-drag
            // value until the next layout pass, so reading it here would undo the drag.
            double dragged = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
            if (dragged < SidebarSnapWidth)
            {
                _settings.ShowPlaylist = false;
                SetPlaylistVisible();
                QueuePreferences();
                return;
            }
            double width = Math.Max(SidebarMinWidth, dragged);
            column.Width = new GridLength(width);
            // Stay at zero: the splitter reads this when the next drag begins, and a
            // real floor there is what stops the sidebar being dragged shut.
            column.MinWidth = 0;
            _sidebarWidth = width;
            QueuePreferences();
        }

        private void UpdateSidebarBounds()
        {
            Grid body = Find<Grid>("BodyGrid");
            if (body.ActualWidth < 1 || _fullscreen || _focusMode || _sidebarDragging) return;
            double maximum = Math.Max(SidebarMinWidth, Math.Min(520, body.ActualWidth - 366));
            _sidebarWidth = Math.Max(SidebarMinWidth, Math.Min(maximum, _sidebarWidth));
            if (_settings.ShowPlaylist && !_sidebarAnimating)
            {
                body.ColumnDefinitions[0].MaxWidth = maximum;
                body.ColumnDefinitions[0].Width = new GridLength(_sidebarWidth);
            }
        }

        private void SetPlaylistVisible()
        {
            if (_focusMode) { SetFocusSidebar(); return; }
            Border normalPanel = Find<Border>("PlaylistPanel");
            Grid.SetColumnSpan(normalPanel, 1); Panel.SetZIndex(normalPanel, 0);
            normalPanel.Width = Double.NaN; normalPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid body = Find<Grid>("BodyGrid");
            Border panel = Find<Border>("PlaylistPanel");
            GridSplitter splitter = Find<GridSplitter>("SidebarSplitter");
            bool visible = _settings.ShowPlaylist && !_fullscreen;
            ColumnDefinition column = body.ColumnDefinitions[0];
            _sidebarAnimating = true;
            column.MinWidth = 0;
            column.MaxWidth = Math.Max(210, Math.Min(520, body.ActualWidth - 366));
            body.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            splitter.IsEnabled = visible;
            if (visible)
            {
                panel.Visibility = Visibility.Visible;
                splitter.Visibility = Visibility.Visible;
                body.ColumnDefinitions[1].Width = new GridLength(6);
            }
            UiMotion.SetColumnWidth(column, visible ? _sidebarWidth : 0, delegate
            {
                // Commit the final layout explicitly, including constraints.
                // An old minimum or an animated effective width must not reserve space.
                bool stillVisible = _settings.ShowPlaylist && !_fullscreen;
                if (visible != stillVisible) return;
                _sidebarAnimating = false;
                if (visible)
                {
                    column.Width = new GridLength(_sidebarWidth);
                    column.MinWidth = 0;
                    // The first call runs before layout, when the body has no width and
                    // the maximum collapses to the minimum. Recompute now it is real.
                    UpdateSidebarBounds();
                }
                else
                {
                    column.MinWidth = 0;
                    column.MaxWidth = 0;
                    column.Width = new GridLength(0);
                    panel.Visibility = Visibility.Collapsed;
                    splitter.Visibility = Visibility.Collapsed;
                    body.ColumnDefinitions[1].Width = new GridLength(0);
                }
            });
        }        private void SetGlass(bool enabled)
        {
            if (!_glass) return;
            Background = enabled ? Brushes.Transparent : _solidBackground;
            _root.Background = enabled ? _glassScrim : _solidBackground;
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _focusMode) { ToggleFocus(); e.Handled = true; return; }
            if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox || Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is GridSplitter) return;
            foreach (KeyValuePair<PlayerAction, ShortcutBinding> pair in _settings.Shortcuts)
            {
                if (!pair.Value.Matches(e)) continue;
                e.Handled = true;
                if (e.IsRepeat && pair.Key != PlayerAction.SeekForward && pair.Key != PlayerAction.SeekBackward
                    && pair.Key != PlayerAction.FrameForward && pair.Key != PlayerAction.FrameBackward
                    && pair.Key != PlayerAction.VolumeUp && pair.Key != PlayerAction.VolumeDown) return;
                switch (pair.Key)
                {
                    case PlayerAction.FrameForward: StepFrame(1); break;
                    case PlayerAction.FrameBackward: StepFrame(-1); break;
                    case PlayerAction.ToggleFocus: ToggleFocus(); break;
                    case PlayerAction.TogglePlayPause: TogglePlay(); break;
                    case PlayerAction.Next: Navigate(1, false); break;
                    case PlayerAction.Previous: Navigate(-1, false); break;
                    case PlayerAction.SeekForward: SeekBy(10); break;
                    case PlayerAction.SeekBackward: SeekBy(-10); break;
                    case PlayerAction.SpeedUp: SetSpeed(_settings.PlaybackSpeed + 0.25); break;
                    case PlayerAction.SpeedDown: SetSpeed(_settings.PlaybackSpeed - 0.25); break;
                    case PlayerAction.ResetSpeed: SetSpeed(1); break;
                    case PlayerAction.VolumeUp: ChangeVolume(0.05); break;
                    case PlayerAction.VolumeDown: ChangeVolume(-0.05); break;
                    case PlayerAction.ToggleMute: ToggleMute(); break;
                    case PlayerAction.ToggleFullscreen: ToggleFullscreen(); break;
                    case PlayerAction.ToggleAbLoop: CycleAbLoop(); break;
                    case PlayerAction.ExportFrame: ExportFrame(); break;
                    case PlayerAction.OpenFiles: ChooseFiles(); break;
                    case PlayerAction.OpenFolder: ChooseFolder(); break;
                    case PlayerAction.TogglePlaylist: TogglePlaylist(); break;
                }
                return;
            }
        }
    }
}
