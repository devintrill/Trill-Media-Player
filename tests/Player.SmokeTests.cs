using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Trill;

public static class PlayerSmokeTests
{
    private static int assertions;
    private static MainWindow window;

    [STAThread]
    public static int Main(string[] args)
    {
        string fixtureRoot = args[0];
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            window = new MainWindow(new string[0], Path.Combine(fixtureRoot, "settings-" + Guid.NewGuid().ToString("N") + ".txt"));
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000;
            window.Show();
            Pump(150);
            Assert(window.IsLoaded, "Window loads its embedded XAML");
            Assert(Field<ObservableCollection<MediaItem>>("_items").Count == 0, "New session has an empty playlist");
            Render((FrameworkElement)window.Content, Path.Combine(fixtureRoot, "welcome.png"));
            TestOpenButtons();

            string photoOne = Path.Combine(fixtureRoot, "photo1.png");
            string photoTwo = Path.Combine(fixtureRoot, "photo2.png");
            MakePhoto(photoOne, 32, 105, 82);
            MakePhoto(photoTwo, 61, 61, 122);
            Call("ImportPaths", new object[] { new string[] { photoTwo, photoOne } });
            Assert(!Field<bool>("_opened"), "Photo decoding does not block the UI thread");
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 5000);
            Assert(Field<ObservableCollection<MediaItem>>("_items").Count == 2, "Imports a natural-order photo playlist");
            Assert(Field<MediaItem>("_current").Name == "photo1.png", "First photo is numerically first");
            Assert(Field<bool>("_opened") && Field<bool>("_playing"), "Photo opens with the transport active");
            Assert(Field<Image>("_photo").Source != null, "Photo is decoded");
            Assert(((BitmapSource)Field<Image>("_photo").Source).PixelWidth == 480, "Small photos are not upscaled in memory");
            Assert(!Field<Slider>("_seek").IsEnabled, "Video seeking disabled for photos");
            using (new FileStream(photoOne, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Assert(true, "Photo file handle is released after decoding");
            Call("Navigate", 1, false);
            Call("Navigate", -1, false);
            Call("Navigate", 1, false);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 5000);
            Assert(Field<MediaItem>("_current").Name == "photo2.png", "Rapid photo navigation keeps the latest selection");
            byte[] chosenPixel = new byte[4];
            var chosenImage = new FormatConvertedBitmap((BitmapSource)Field<Image>("_photo").Source, PixelFormats.Bgra32, null, 0);
            chosenImage.CopyPixels(new Int32Rect(0, 0, 1, 1), chosenPixel, 4, 0);
            Assert(chosenPixel[0] == 122 && chosenPixel[1] == 61 && chosenPixel[2] == 61, "An obsolete photo decode cannot replace the latest image");
            Call("Navigate", -1, false);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 5000);
            Pump(120);
            // A still photo has no timeline, so play/pause is deliberately inert on one.
            Call("TogglePlay");
            Assert(Field<bool>("_playing"), "Play and pause do nothing while a still photo is shown");
            Call("Navigate", 1, false);
            Assert(Field<MediaItem>("_current").Name == "photo2.png", "Next selects the following item");
            Call("Navigate", -1, false);
            Assert(Field<MediaItem>("_current").Name == "photo1.png", "Previous selects the prior item");
            AppSettings settings = Field<AppSettings>("_settings");
            settings.LoopPlaylist = false;
            Call("ApplySettings");
            Call("TogglePlay");
            // Photos are held until the viewer moves on; only video and audio complete.
            Pump(1400);
            Assert(Field<MediaItem>("_current").Name == "photo1.png", "A photo is held instead of advancing on a timer");
            Assert(Field<bool>("_playing"), "Holding a photo leaves the transport running");
            Call("Navigate", 1, false);
            Assert(Field<MediaItem>("_current").Name == "photo2.png", "Next moves past a held photo");
            Call("Navigate", 1, false);
            Assert(Field<MediaItem>("_current").Name == "photo2.png", "End of a non-looping playlist stays put");
            settings.LoopPlaylist = true;
            Call("Navigate", 1, false);
            Assert(Field<MediaItem>("_current").Name == "photo1.png", "Looping returns to first item");
            Call("TogglePlay");
            Call("SetSpeed", 2.0);
            Assert(Field<MediaElement>("_video").SpeedRatio == 2, "Playback speed is applied");
            Call("SetSpeed", 50.0);
            Assert(settings.PlaybackSpeed == 4, "Speed upper bound is enforced");
            Call("SetSpeed", 0.01);
            Assert(settings.PlaybackSpeed == .25, "Speed lower bound is enforced");
            Call("SetSpeed", 1.0);
            Call("ToggleMute");
            Assert(Field<MediaElement>("_video").IsMuted, "Mute reaches media engine");
            Call("ToggleMute");
            bool playlistWasVisible = settings.ShowPlaylist;
            Call("TogglePlaylist");
            Assert(settings.ShowPlaylist != playlistWasVisible, "Playlist visibility toggles");
            Call("TogglePlaylist");
            Render((FrameworkElement)window.Content, Path.Combine(fixtureRoot, "photo.png"));

            var settingsWindow = new SettingsWindow(settings);
            settingsWindow.ShowInTaskbar = false;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            settingsWindow.Left = -20000; settingsWindow.Top = -20000;
            settingsWindow.Show(); Pump(70);
            Render((FrameworkElement)settingsWindow.Content, Path.Combine(fixtureRoot, "settings.png"));
            settingsWindow.Close();
            Assert(true, "Settings editor renders successfully");
            var picker = new LocalPickerWindow(false);
            picker.ShowInTaskbar = false;
            picker.WindowStartupLocation = WindowStartupLocation.Manual;
            picker.Left = -20000; picker.Top = -20000;
            picker.Show(); Pump(70); picker.Close();
            Assert(true, "Custom local picker loads successfully");
            TestWindowsPickers();



            Call("ClearPlaylist");
            string videoPath = Path.Combine(fixtureRoot, "clip.avi");
            MakeAvi(videoPath);
            Call("ImportPaths", new object[] { new string[] { videoPath, photoOne } });
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 10000);
            Assert(Field<bool>("_opened") && !Field<bool>("_failed"), "Generated AVI opens in Windows media engine");
            Assert(Field<MediaItem>("_current").IsVideo, "Mixed playlist selects video");
            Assert(Field<Slider>("_seek").IsEnabled, "Video timeline can be scrubbed");

            // Video thumbnails decode through Media Foundation on the shared worker,
            // not a hidden media element parked off screen.
            BitmapSource videoTile = null;
            Thumbnails.Request(videoPath, MediaKind.Video, delegate(BitmapSource image) { videoTile = image; });
            PumpUntil(delegate { return videoTile != null; }, 15000);
            Assert(videoTile != null, "A video thumbnail decodes without a hidden player window");
            Assert(videoTile.PixelWidth > 1 && videoTile.PixelHeight > 1, "The video thumbnail has real dimensions");
            Assert(videoTile.IsFrozen, "Video thumbnails are frozen for use on the UI thread");
            Assert(Math.Max(videoTile.PixelWidth, videoTile.PixelHeight) <= 336, "Video thumbnails decode at tile size, not full resolution");
            var tilePixels = new byte[videoTile.PixelWidth * 4];
            new FormatConvertedBitmap(videoTile, PixelFormats.Bgra32, null, 0)
                .CopyPixels(new Int32Rect(0, videoTile.PixelHeight / 2, videoTile.PixelWidth, 1), tilePixels, videoTile.PixelWidth * 4, 0);
            int brightest = 0;
            foreach (byte channel in tilePixels) if (channel > brightest) brightest = channel;
            Assert(brightest > 0, "The video thumbnail carries picture data rather than a black frame");

            // --- A-B loop: one button cycles start, end, clear ---
            MediaElement engine = Field<MediaElement>("_video");
            if (!Field<bool>("_playing")) Call("TogglePlay");
            engine.Position = TimeSpan.FromSeconds(2); Pump(120);
            Call("CycleAbLoop");
            Assert(Field<double>("_loopStart") > 1 && Field<double>("_loopEnd") < 0, "The first press marks the loop start");
            engine.Position = TimeSpan.FromSeconds(5); Pump(120);
            Call("CycleAbLoop");
            Assert(Field<double>("_loopEnd") > Field<double>("_loopStart"), "The second press marks the loop end");
            engine.Position = TimeSpan.FromSeconds(4.9); Pump(120);
            PumpUntil(delegate { return Field<MediaElement>("_video").Position.TotalSeconds < 4; }, 5000);
            Assert(engine.Position.TotalSeconds < 4, "Reaching the end of an A-B loop jumps back to the start");
            Call("CycleAbLoop");
            Assert(Field<double>("_loopStart") < 0 && Field<double>("_loopEnd") < 0, "The third press clears the loop");

            // --- frame export: native resolution, and the frame actually on screen ---
            string exportFolder = (string)typeof(MainWindow)
                .GetProperty("ExportFolder", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            var existingExports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(exportFolder))
                foreach (string had in Directory.GetFiles(exportFolder, "*.png")) existingExports.Add(had);
            if (Field<bool>("_playing")) Call("TogglePlay");
            engine.Position = TimeSpan.FromSeconds(8.05); Pump(200);
            Call("ExportFrame");
            PumpUntil(delegate { return !Field<bool>("_exporting"); }, 30000);
            Assert(!Field<bool>("_exporting"), "Frame export finishes without blocking the UI thread");
            string written = null;
            if (Directory.Exists(exportFolder))
                foreach (string found in Directory.GetFiles(exportFolder, "*.png"))
                    if (!existingExports.Contains(found)) written = found;
            Assert(written != null, "Exporting writes a PNG into the Pictures Trill folder");
            if (written != null)
            {
                BitmapSource saved;
                using (var pngStream = new FileStream(written, FileMode.Open, FileAccess.Read))
                    saved = BitmapFrame.Create(pngStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Assert(saved.PixelWidth == 32 && saved.PixelHeight == 32, "The export keeps the video's own resolution");
                var savedPixel = new byte[4];
                new FormatConvertedBitmap(saved, PixelFormats.Bgra32, null, 0)
                    .CopyPixels(new Int32Rect(1, 1, 1, 1), savedPixel, 4, 0);
                // The fixture encodes its frame number in blue; 8.05s at 5 fps is frame 40.
                Assert(savedPixel[0] == 80, "The export saves the frame shown at that position, not a nearby one");
                File.Delete(written);
            }
            engine.Position = TimeSpan.Zero;
            if (!Field<bool>("_playing")) Call("TogglePlay");
            Pump(120);
            Call("TogglePlay");
            Call("Navigate", 1, false);
            Call("Navigate", -1, false);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 10000);
            Pump(250);
            Assert(Field<bool>("_opened") && !Field<bool>("_playing"), "Paused navigation opens a video without starting playback");
            Assert(Field<MediaElement>("_video").Position.TotalSeconds < .2, "Paused video stays at its first frame");
            Call("TogglePlay");
            Pump(400);
            Assert(Field<MediaElement>("_video").Position.TotalSeconds > 0, "Video position advances while playing");
            Call("TogglePlay"); Pump(100);
            double beforeSeek = Field<MediaElement>("_video").Position.TotalSeconds;
            Pump(180);
            Assert(Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - beforeSeek) < .15, "Video pause freezes position");
            Call("SeekBy", 10.0); Pump(160);
            Assert(Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - beforeSeek - 10) < .5, "Forward shortcut seeks ten seconds");
            Call("SeekBy", -10.0); Pump(160);
            Assert(Field<MediaElement>("_video").Position.TotalSeconds < 1.5, "Backward shortcut seeks ten seconds");
            Slider timeline = Field<Slider>("_seek");
            var track = (Track)timeline.Template.FindName("PART_Track", timeline);
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            Assert(Field<bool>("_scrubbing"), "Thumb drag starts scrub mode through its routed event");
            double dragStart = Field<MediaElement>("_video").Position.TotalSeconds;
            for (int i = 0; i < 300; i++) timeline.Value = 2 + i / 30.0;
            timeline.Value = 8;
            Pump(180);
            Assert(Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - dragStart) < .15, "Hundreds of drag updates do not flood the media engine with seeks");
            var playerGrid = (Grid)window.Content;
            Assert(((TextBlock)playerGrid.FindName("PositionLabel")).Text == "0:08", "Scrub time follows the thumb immediately");
            Call("Tick");
            Assert(timeline.Value == 8, "Playback timer does not move the thumb during a drag");
            TestLiveScrub(timeline);
            timeline.Value = 8;
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump(150);
            Assert(!Field<bool>("_scrubbing") && !Field<bool>("_playing"), "Releasing a paused drag preserves pause");
            Assert(Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - 8) < .25, "Release seeks to the chosen position");
            Call("StepFrame", 1); Pump(250);
            Assert(!Field<bool>("_playing") && Math.Abs(Field<double>("_frameStepTarget") - 8.2) < .001, "Frame step pauses and advances exactly one source frame");
            Call("StepFrame", 1); Pump(150); Call("StepFrame", -1); Pump(250);
            Assert(Math.Abs(Field<double>("_frameStepTarget") - 8.2) < .001, "Frame step reverses to the adjacent frame");
            object shown = typeof(MainWindow).GetField("_shownScrubFrame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Assert(shown != null && Math.Abs((double)shown.GetType().GetField("Time").GetValue(shown) - 8.2) < .01, "Frame step displays the decoded target frame");
            Call("TogglePlay");
            Call("BeginScrub"); Pump(100);
            double playingDragStart = Field<MediaElement>("_video").Position.TotalSeconds;
            timeline.Value = 3;
            Pump(150);
            Assert(Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - playingDragStart) < .15, "Playback clock remains paused while live preview frames change");
            Call("EndScrub"); Pump(350);
            Assert(Field<bool>("_playing") && Field<MediaElement>("_video").Position.TotalSeconds > 3.1, "Playing video resumes at the released position");
            Call("BeginScrub");
            Call("TogglePlay");
            timeline.Value = 4;
            Call("EndScrub"); Pump(150);
            Assert(!Field<bool>("_playing") && Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - 4) < .25, "Pause requested during drag remains paused after release");
            Call("BeginScrub"); timeline.Value = 12;
            Call("Navigate", 1, false);
            Call("EndScrub");
            Assert(!Field<bool>("_scrubbing") && !Field<MediaItem>("_current").HasTimeline, "Changing media cancels any stale scrub");
            Assert(((Image)((Grid)window.Content).FindName("ScrubImage")).Source == null, "Changing media removes cached preview frames immediately");
            Call("Navigate", -1, false);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 10000);
            Pump(100);
            Call("SetSpeed", 2.0);
            Call("TogglePlay");
            Pump(500);
            Assert(Field<MediaElement>("_video").Position.TotalSeconds >= .7, "Video advances at increased speed");
            // Repeat mode 1 is the default, so a finished video restarts rather than advancing.
            Call("SeekBy", 1000.0);
            Pump(1200);
            Assert(Field<MediaItem>("_current").IsVideo, "A finished video repeats itself by default");
            SetField("_repeatMode", 2);
            Call("SeekBy", 1000.0);
            PumpUntil(delegate { return !Field<MediaItem>("_current").IsVideo; }, 2500);
            Assert(!Field<MediaItem>("_current").IsVideo, "Loop-playlist mode advances to the photo in a mixed playlist");
            SetField("_repeatMode", 1);

            string tonePath = Path.Combine(fixtureRoot, "tone.wav");
            MakeTone(tonePath, 4, 1000);
            Call("ImportPaths", new object[] { new string[] { tonePath } });
            MediaItem tone = null;
            foreach (MediaItem candidate in Field<ObservableCollection<MediaItem>>("_items"))
                if (candidate.IsAudio) tone = candidate;
            Assert(tone != null, "A WAV file imports as audio");
            // Importing appends, so the audio track has to be selected explicitly.
            Call("OpenMedia", tone, true);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 8000);
            Assert(Field<MediaItem>("_current").IsAudio && !Field<bool>("_failed"), "The WAV opens in the player");
            SpectrumView spectrum = Field<SpectrumView>("_spectrum");
            FieldInfo levelsField = typeof(SpectrumView).GetField("levels", BindingFlags.NonPublic | BindingFlags.Instance);
            PumpUntil(delegate {
                foreach (double value in (double[])levelsField.GetValue(spectrum)) if (value > .4) return true;
                return false;
            }, 10000);
            double[] bands = (double[])levelsField.GetValue(spectrum);
            int peakBand = 0;
            for (int i = 0; i < bands.Length; i++) if (bands[i] > bands[peakBand]) peakBand = i;
            Assert(bands[peakBand] > .4, "Local audio decoding produces a real spectrum peak");
            Assert(peakBand >= 23 && peakBand <= 27, "The spectrum peak matches the 1 kHz test tone");
            float[] waveform = (float[])typeof(SpectrumView).GetField("wave", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spectrum);
            float wavePeak = 0;
            foreach (float sample in waveform) wavePeak = Math.Max(wavePeak, Math.Abs(sample));
            Assert(wavePeak > .1f, "Waveform displays decoded source samples");
            Render((FrameworkElement)window.Content, Path.Combine(fixtureRoot, "audio.png"));
            Call("SeekBy", 1000.0);
            Pump(1000);
            Assert(Field<MediaItem>("_current") == tone && Field<bool>("_playing") &&
                Field<MediaElement>("_video").Position.TotalSeconds < 3, "Audio loops automatically");
            double windowedWidth = window.ActualWidth;
            Call("ToggleFullscreen");
            Pump(600);
            Assert(Field<bool>("_fullscreen"), "Fullscreen mode engages");
            Assert(window.WindowStyle == WindowStyle.None, "Fullscreen removes the window chrome");
            var fullscreenBody = (Grid)((Grid)window.Content).FindName("BodyGrid");
            Assert(fullscreenBody.ColumnDefinitions[0].ActualWidth == 0 && fullscreenBody.ColumnDefinitions[1].ActualWidth == 0,
                "Fullscreen leaves zero space for the sidebar and divider");
            Assert(window.ActualWidth >= SystemParameters.PrimaryScreenWidth - 2 &&
                window.ActualHeight >= SystemParameters.PrimaryScreenHeight - 2, "Fullscreen covers the whole screen");
            Call("ToggleFullscreen");
            Pump(600);
            Assert(!Field<bool>("_fullscreen"), "Fullscreen exits");
            Assert(window.WindowStyle == WindowStyle.SingleBorderWindow, "Leaving fullscreen restores the window chrome");
            Assert(Math.Abs(window.ActualWidth - windowedWidth) < 2, "Leaving fullscreen restores the previous size");


            Call("ClearPlaylist");
            TestMediaWorkspace(fixtureRoot, photoOne, photoTwo);
            TestComfortFeatures(fixtureRoot, photoOne, photoTwo, videoPath);
            string broken = Path.Combine(fixtureRoot, "broken.png");
            File.WriteAllText(broken, "not an image");
            Call("ImportPaths", new object[] { new string[] { broken, photoOne } });
            PumpUntil(delegate { return Field<bool>("_failed"); }, 5000);
            Assert(Field<bool>("_failed") && !Field<bool>("_playing"), "Broken media shows recoverable error");
            Call("Navigate", 1, false);
            PumpUntil(delegate { return Field<bool>("_opened") || Field<bool>("_failed"); }, 5000);
            Assert(Field<bool>("_opened") && !Field<bool>("_failed"), "Next recovers after broken media");
            Call("ClearPlaylist");
            Assert(Field<Image>("_photo").Source == null && Field<MediaElement>("_video").Source == null, "Clear releases all active media sources");
            Assert(Field<ObservableCollection<MediaItem>>("_items").Count == 0, "Clear forgets playlist paths");
            window.Close();
            string savedSettings = Field<string>("_settingsPath");
            AppSettings persisted = SettingsStore.Load(savedSettings);
            Assert(persisted.SidebarWidth == Field<double>("_sidebarWidth"), "Closing saves the actual sidebar width");
            window = new MainWindow(new string[0], savedSettings) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            window.Show(); Pump(300);
            Assert(Field<double>("_sidebarWidth") == persisted.SidebarWidth && Field<bool>("_playlistTiles") == persisted.PlaylistTiles,
                "A new app window restores saved sidebar layout");
            window.Close();            Console.WriteLine("PASS: " + assertions + " player integration assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (window != null) window.Close();
            return 1;
        }
    }

    private static void TestComfortFeatures(string root, string photoOne, string photoTwo, string video)
    {
        Call("ClearPlaylist");
        Field<AppSettings>("_settings").IsMuted = true;
        Call("ImportPaths", new object[] { new string[] { video, photoOne, photoTwo } });
        PumpUntil(delegate { return Field<bool>("_opened"); }, 8000);
        Call("TogglePlay"); Call("PrepareNeighbors");
        var warm = Field<System.Collections.IDictionary>("_warmMedia");
        PumpUntil(delegate { return warm.Contains(photoOne) && WarmReady(warm[photoOne]); }, 5000);
        Assert(warm.Contains(photoOne) && WarmReady(warm[photoOne]), "The neighboring photo is decoded before navigation");
        object photoSlot = warm[photoOne];
        var readyPhoto = (BitmapSource)photoSlot.GetType().GetField("Photo").GetValue(photoSlot);
        Call("Navigate", 1, false);
        PumpUntil(delegate { return Field<bool>("_opened"); }, 5000);
        Assert(Object.ReferenceEquals(Field<Image>("_photo").Source, readyPhoto), "Navigation reuses the preloaded photo bitmap");
        Call("PrepareNeighbors");
        PumpUntil(delegate { return warm.Contains(video) && WarmReady(warm[video]); }, 8000);
        Assert(warm.Contains(video) && WarmReady(warm[video]), "Neighboring video is prepared while the photo is shown");
        var preparedPlayer = (MediaElement)warm[video].GetType().GetField("Player").GetValue(warm[video]);
        Assert(preparedPlayer.IsMuted && preparedPlayer.Volume == 0, "Preloaded video cannot produce sound");
        Call("Navigate", -1, false);
        Assert(Field<MediaElement>("_video") == preparedPlayer && Field<bool>("_opened"), "Video navigation adopts the ready playback engine");
        Assert(!Field<bool>("_playing"), "Preloaded video preserves paused navigation");
        Assert(warm.Count <= 2, "Preloading retains at most two neighbors");

        Grid ui = (Grid)window.Content;
        Slider seek = Field<Slider>("_seek");
        seek.Value = 8;
        Call("BeginScrub");
        Assert(Field<Border>("_seekLens").Visibility == Visibility.Visible, "Dragging opens the magnified timeline");
        Assert(Field<double>("_lensSpan") < Field<double>("_duration"), "Magnified timeline covers a smaller time range");
        double before = seek.Value;
        Call("MoveLensPointer", 10.0, -30.0, false);
        Assert(Field<bool>("_lensFine") && seek.Value > before && seek.Value < before + .2, "Moving into the lens enables precise seeking without Shift");
        Render((FrameworkElement)window.Content, Path.Combine(root, "timeline-lens.png"));
        Call("EndScrub");
        Assert(Field<Border>("_seekLens").Visibility == Visibility.Collapsed, "Releasing the timeline hides the lens");
        Pump(250);

        ListBox playlist = Field<ListBox>("_playlist");
        playlist.UpdateLayout();
        var row = (ListBoxItem)playlist.ItemContainerGenerator.ContainerFromIndex(0);
        Image thumbnail = Descendant<Image>(row);
        Border thumbnailBorder = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(thumbnail));
        SetField("_hoverEntry", playlist.Items[0]);
        SetField("_hoverThumbnail", thumbnailBorder);
        SetField("_hoverRatio", .5);
        MediaItem active = Field<MediaItem>("_current");
        double position = Field<MediaElement>("_video").Position.TotalSeconds;
        Call("BeginHover");
        PumpUntil(delegate { return PreviewBlue(Field<Image>("_hoverImage")) == 100; }, 7000);
        Assert(PreviewBlue(Field<Image>("_hoverImage")) == 100, "Hover preview displays the frame at the thumbnail pointer position");
        Assert(Field<MediaItem>("_current") == active && Math.Abs(Field<MediaElement>("_video").Position.TotalSeconds - position) < .05,
            "Hover preview does not change active playback");
        object hoverCache = Field<object>("_hoverPreview");
        long bytes = (long)hoverCache.GetType().GetField("cacheBytes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hoverCache);
        Assert(bytes <= 8L * 1024 * 1024, "Hover preview has its own small memory limit");
        Call("StopHover"); Pump(100);
        Assert(Field<Image>("_hoverImage").Source == null && Field<Border>("_hoverSurface").Visibility == Visibility.Collapsed, "Leaving a thumbnail clears the hover image");

        bool sidebarWasVisible = Field<AppSettings>("_settings").ShowPlaylist;
        ((Button)ui.FindName("FocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(300);
        Grid body = (Grid)ui.FindName("BodyGrid");
        Assert(Field<bool>("_focusMode") && ((Border)ui.FindName("TransportBar")).Visibility == Visibility.Hidden, "Focus mode hides transport controls");
        Assert(body.ColumnDefinitions[0].ActualWidth == 0 && ((Border)ui.FindName("PlaylistPanel")).Visibility == Visibility.Collapsed,
            "Focus mode gives the full width to media");
        Call("FocusPointer", new Point(window.ActualWidth / 2, window.ActualHeight / 2));
        Assert(((Border)ui.FindName("TransportBar")).Visibility == Visibility.Hidden, "Movement over the media leaves focus controls hidden");
        Render((FrameworkElement)window.Content, Path.Combine(root, "focus.png"));
        Call("FocusPointer", new Point(ui.ActualWidth / 2, ui.ActualHeight - 2)); Pump(100);
        Assert(((Border)ui.FindName("TransportBar")).Visibility == Visibility.Visible, "Bottom edge reveals transport controls");
        Call("FocusPointer", new Point(2, 200)); Pump(100);
        Assert(((Border)ui.FindName("PlaylistPanel")).Visibility == Visibility.Visible && body.ColumnDefinitions[0].ActualWidth == 0,
            "Left edge reveals the sidebar as an overlay without shrinking media");
        Call("FocusPointer", new Point(window.ActualWidth - 20, 2));
        Assert(((Button)ui.FindName("ZoomResetButton")).Opacity == 1, "Top edge reveals zoom controls");
        Call("ToggleFocus"); Pump(300);
        Assert(((Border)ui.FindName("TransportBar")).Visibility == Visibility.Visible && Field<AppSettings>("_settings").ShowPlaylist == sidebarWasVisible,
            "Leaving focus mode restores the prior layout");
        Call("ClearPlaylist");
        Assert(warm.Count == 0 && Field<Image>("_switchCover").Source == null, "Clearing media releases preloads and transition images");
    }

    private static bool WarmReady(object slot) { return (bool)slot.GetType().GetField("Ready").GetValue(slot); }
    private static void TestMediaWorkspace(string root, string photoOne, string photoTwo)
    {
        Call("ImportPaths", new object[] { new string[] { photoOne, photoTwo } });
        PumpUntil(delegate { return Field<bool>("_opened"); }, 5000);
        Grid ui = (Grid)window.Content;
        Grid stage = (Grid)ui.FindName("Stage");
        Call("ZoomMedia", 2.0, new Point(stage.ActualWidth / 2, stage.ActualHeight / 2));
        Pump(150);
        Assert(Field<double>("_zoom") == 2 && Field<ScaleTransform>("_mediaScale").ScaleX == 2, "Wheel zoom enlarges the photo");
        Assert((string)((Button)ui.FindName("ZoomResetButton")).Content == "200%", "Zoom percentage follows the media scale");
        ((Button)ui.FindName("ZoomResetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(150);
        Assert(Field<double>("_zoom") == 1, "Zoom reset returns to fit");
        Call("ZoomMedia", .01, new Point(0, 0)); Pump(150);
        Assert(Field<double>("_zoom") == 1 && Field<TranslateTransform>("_mediaPan").X == 0, "Zoom out stops at fitted size and resets pan");
        Call("ZoomMedia", 2.0, new Point(0, 0)); Pump(150);
        Call("Navigate", 1, false);
        Assert(Field<double>("_zoom") == 1, "Changing media restores the fitted view");
        PumpUntil(delegate { return Field<bool>("_opened"); }, 5000);

        Pump(250);
        var body = (Grid)ui.FindName("BodyGrid");
        var splitter = (GridSplitter)ui.FindName("SidebarSplitter");
        double before = body.ColumnDefinitions[0].ActualWidth;
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(70, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(70, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump(100); Call("RememberSidebarWidth");
        Assert(body.ColumnDefinitions[0].ActualWidth > before + 40, "Dragging the sidebar divider resizes the sidebar");
        double resized = body.ColumnDefinitions[0].ActualWidth;
        Call("TogglePlaylist"); Pump(300);
        Assert(body.ColumnDefinitions[0].ActualWidth == 0 && body.ColumnDefinitions[1].ActualWidth == 0, "Closing the sidebar leaves zero reserved width after dragging");
        Assert(stage.TranslatePoint(new Point(0, 0), ui).X < 3, "Hidden sidebar lets the stage reach the left edge");
        Call("TogglePlaylist"); Pump(300);
        Assert(Math.Abs(body.ColumnDefinitions[0].ActualWidth - resized) < 2, "Hiding and reopening preserves the sidebar width");

        // A partial drag springs back to the minimum, but dragging it right down
        // closes the sidebar rather than parking it at that minimum.
        double narrowBy = body.ColumnDefinitions[0].ActualWidth - 170;
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(-narrowBy, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(-narrowBy, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump(200);
        Assert(body.ColumnDefinitions[0].ActualWidth >= 209, "A partial drag snaps back to the minimum sidebar width");
        Assert(Field<AppSettings>("_settings").ShowPlaylist, "A partial drag leaves the sidebar open");

        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(-600, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(-600, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump(500);
        Assert(body.ColumnDefinitions[0].ActualWidth == 0 && body.ColumnDefinitions[1].ActualWidth == 0,
            "Dragging the divider shut closes the sidebar completely");
        Assert(!Field<AppSettings>("_settings").ShowPlaylist, "Dragging the sidebar shut turns the playlist off");
        Assert(stage.TranslatePoint(new Point(0, 0), ui).X < 3, "A dragged-shut sidebar lets the stage reach the left edge");
        Call("TogglePlaylist"); Pump(400);
        Assert(body.ColumnDefinitions[0].ActualWidth >= 200, "Reopening after a drag-close restores a usable width");
        Assert(((Border)ui.FindName("TransportBar")).ActualHeight <= 45, "Transport controls occupy a single slim row");
        Call("TogglePlaylist"); Call("TogglePlaylist"); Call("TogglePlaylist"); Pump(300);
        Assert(body.ColumnDefinitions[0].ActualWidth == 0, "Rapid sidebar toggles finish completely closed");
        Call("ToggleFullscreen"); Pump(300);
        Assert(body.ColumnDefinitions[0].ActualWidth == 0 && stage.TranslatePoint(new Point(0, 0), ui).X <= 1, "Fullscreen media reaches the screen edge with a previously hidden sidebar");
        Call("ToggleFullscreen"); Pump(300);
        Assert(body.ColumnDefinitions[0].ActualWidth == 0, "Leaving fullscreen preserves a hidden sidebar");
        Call("TogglePlaylist"); Pump(300);
        ((Button)ui.FindName("PlaylistLayoutButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(350);
        ListBox list = Field<ListBox>("_playlist");
        var tiles = Descendant<VirtualizingTilePanel>(list);
        Assert(tiles != null && tiles.FixedColumns == 2, "Playlist layout button enables exactly two thumbnail columns");
        var firstTile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var secondTile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        Point firstAt = firstTile.TranslatePoint(new Point(0,0), list), secondAt = secondTile.TranslatePoint(new Point(0,0), list);
        Assert(secondAt.X > firstAt.X + 70 && Math.Abs(secondAt.Y - firstAt.Y) < 1, "Two playlist items render side by side");
        Image tileImage = Descendant<Image>(firstTile);
        Assert(tileImage.ActualWidth > 90 && tileImage.ActualHeight >= 80, "Grid thumbnails are substantially larger");
        Render((FrameworkElement)window.Content, Path.Combine(root, "playlist-grid.png"));
        ((Button)ui.FindName("PlaylistLayoutButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(200);
        Assert(Descendant<VirtualizingStackPanel>(list) != null, "Layout toggle returns to the scrolling list");
        var rowItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        Assert(((Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(Descendant<Image>(rowItem)))).ActualWidth == 88, "List view also uses larger thumbnails");

        ((Button)ui.FindName("ShuffleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        MediaItem current = Field<MediaItem>("_current");
        Call("Navigate", 1, false);
        Assert(Field<MediaItem>("_current") != current, "Shuffle moves to a different item");
        Call("Navigate", -1, false);
        Assert(Field<MediaItem>("_current") == current, "Shuffle previous follows playback history");
        ((Button)ui.FindName("ShuffleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        int repeat = Field<int>("_repeatMode");
        for (int i = 0; i < 3; i++) ((Button)ui.FindName("LoopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Field<int>("_repeatMode") == repeat, "Loop button cycles all three modes");

        TextBox search = (TextBox)ui.FindName("PlaylistSearch");
        search.Text = "photo1"; Pump(200);
        Assert(Field<System.Collections.IList>("_sidebarEntries").Count == 1, "Search filters the visible playlist");
        Assert(Field<ObservableCollection<MediaItem>>("_items").Count == 2, "Search preserves the complete playback queue");
        search.Text = "no-such-media"; Pump(200);
        Assert(((TextBlock)ui.FindName("PlaylistEmptyLabel")).Text == "No matches", "Empty search results are clearly indicated");
        ((Button)ui.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(200);
        Assert(Field<System.Collections.IList>("_sidebarEntries").Count == 2, "Clearing search restores all entries");
        Field<AppSettings>("_settings").PlaylistSort = 1; Call("ApplyLibraryView");
        object firstSorted = Field<System.Collections.IList>("_sidebarEntries")[0];
        Assert((string)firstSorted.GetType().GetProperty("Name").GetValue(firstSorted, null) == "photo2.png", "Descending name sort changes row order");
        Assert(Field<ObservableCollection<MediaItem>>("_items")[0].Name == "photo2.png", "Playback queue follows the chosen sort order");
        File.SetLastWriteTimeUtc(photoOne, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(photoTwo, DateTime.UtcNow.AddDays(-1));
        Field<AppSettings>("_settings").PlaylistSort = 2; Call("RefreshSidebar");
        object newest = Field<System.Collections.IList>("_sidebarEntries")[0];
        Assert((string)newest.GetType().GetProperty("Name").GetValue(newest, null) == "photo1.png", "Date sort puts the newest file first");
        Field<AppSettings>("_settings").PlaylistSort = 0; Call("ApplyLibraryView");
        string folder = Path.Combine(root, "tree"), child = Path.Combine(folder, "child"), grandchild = Path.Combine(child, "grandchild");
        Directory.CreateDirectory(grandchild);
        File.Copy(photoOne, Path.Combine(folder, "picture.png"), true);
        Call("BrowseSidebar", folder, false);
        PumpUntil(delegate { return Field<string>("_browserFolder") == folder; }, 5000);
        var rows = Field<System.Collections.IList>("_sidebarEntries");
        Assert(rows.Count == 2, "Sidebar shows media and child folders together");
        Assert(((TextBlock)ui.FindName("CountLabel")).Text == "2", "Folder count describes the visible rows");
        Field<ListBox>("_playlist").SelectedIndex = 0;
        PumpUntil(delegate { return Field<string>("_browserFolder") == child; }, 5000);
        Assert(Field<string>("_browserFolder") == child && rows.Count == 1, "Selecting a folder reveals folders inside it");
        StackPanel crumbs = (StackPanel)ui.FindName("Breadcrumbs");
        Button ancestor = null;
        foreach (object entry in crumbs.Children) { Button button = entry as Button; if (button != null && (string)button.ToolTip == folder) ancestor = button; }
        Assert(ancestor != null, "Folder breadcrumbs include clickable ancestors");
        ancestor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(delegate { return Field<string>("_browserFolder") == folder; }, 5000);
        Assert(Field<string>("_browserFolder") == folder, "Breadcrumb click returns directly to an ancestor");
        Call("BrowseSidebar", child, false);
        PumpUntil(delegate { return Field<string>("_browserFolder") == child; }, 5000);
        ((Button)ui.FindName("UpFolderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(delegate { return Field<string>("_browserFolder") == folder; }, 5000);
        Assert(Field<string>("_browserFolder") == folder, "Parent folder button navigates back up");

        Type store = typeof(MainWindow).Assembly.GetType("Trill.FavoritesStore");
        MethodInfo save = store.GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo load = store.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic);
        string saved = Path.Combine(root, "favorites-test.txt");
        var favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { photoOne, child };
        Assert((bool)save.Invoke(null, new object[] { favorites, saved }), "Favorites save to a local file");
        var reloaded = (HashSet<string>)load.Invoke(null, new object[] { saved });
        Assert(reloaded.SetEquals(favorites), "Both favorite files and folders survive reloading");
        favorites.Remove(photoOne);
        Assert((bool)save.Invoke(null, new object[] { favorites, saved }) &&
            ((HashSet<string>)load.Invoke(null, new object[] { saved })).SetEquals(favorites), "Removing a favorite updates existing saved data");

        double width = window.Width;
        window.Width = window.MinWidth; Pump(150);
        Assert(Math.Abs(((Border)ui.FindName("TransportBar")).ActualWidth - ui.ActualWidth) < 2, "Transport spans the resized window");
        Assert(Math.Abs(body.ActualWidth - body.ColumnDefinitions[0].ActualWidth - body.ColumnDefinitions[1].ActualWidth - body.ColumnDefinitions[2].ActualWidth) < 2, "Media fills the remaining width after resizing");
        Render((FrameworkElement)window.Content, Path.Combine(root, "compact.png"));
        Assert(((Slider)ui.FindName("SeekSlider")).ActualWidth > 100, "Timeline stays usable at the minimum window width");
        window.Width = width;
        Call("ClearPlaylist");
    }
    private static void TestVirtualizedTiles()
    {
        var items = new List<string>();
        for (int i = 0; i < 10000; i++) items.Add("Item " + i);
        var list = new ListBox { ItemsSource = items, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel))) };
        ScrollViewer.SetCanContentScroll(list, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var host = new Window { Content = list, Width = 800, Height = 560, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        var elapsed = Stopwatch.StartNew();
        host.Show(); Pump(100);
        VirtualizingTilePanel panel = Descendant<VirtualizingTilePanel>(list);
        Assert(panel != null, "Thumbnail grid uses the virtualizing panel");
        int rendered = VisualTreeHelper.GetChildrenCount(panel);
        Assert(rendered > 0 && rendered < 40, "Ten thousand entries create fewer than forty tile containers");
        Console.WriteLine("MEASURE: 10,000 items, " + rendered + " visible/buffer containers, initial layout " + elapsed.ElapsedMilliseconds + " ms including 100 ms settle.");
        list.SelectedIndex = items.Count - 1;
        list.ScrollIntoView(items[items.Count - 1]);
        Pump(100);
        Assert(list.ItemContainerGenerator.ContainerFromIndex(9999) != null, "Scrolling directly to the final virtualized item works");
        Assert(VisualTreeHelper.GetChildrenCount(panel) < 40, "Scrolling keeps the number of tile containers bounded");
        panel.SetVerticalOffset(0); Pump(50);
        Assert(list.ItemContainerGenerator.ContainerFromIndex(0) != null, "Scrolling back restores the first row");
        host.Width = 440; Pump(100);
        Assert(panel.ViewportWidth < 500 && VisualTreeHelper.GetChildrenCount(panel) < 25, "Resizing reflows visible tiles without realizing the whole folder");
        list.ItemsSource = new string[] { "one", "two" }; Pump(70);
        Assert(VisualTreeHelper.GetChildrenCount(panel) == 2 && panel.VerticalOffset == 0, "Changing folders resets virtualized layout and scroll position");
        panel.FixedColumns = 2;
        panel.ItemHeight = 144;
        list.ItemsSource = items; Pump(100);
        Assert(VisualTreeHelper.GetChildrenCount(panel) < 20, "Two-column mode keeps a large playlist virtualized");
        list.ScrollIntoView(items[9999]); Pump(100);
        Assert(list.ItemContainerGenerator.ContainerFromIndex(9999) != null, "Two-column scrolling reaches the final media item");
        host.Width = 300; Pump(100);
        Assert(panel.FixedColumns == 2 && VisualTreeHelper.GetChildrenCount(panel) < 20, "Narrowing the sidebar retains two columns with bounded containers");
        host.Close();
    }

    private static void TestFolderLoading(string fixtureRoot)
    {
        string many = Path.Combine(fixtureRoot, "many");
        string last = Path.Combine(fixtureRoot, "last");
        Directory.CreateDirectory(many); Directory.CreateDirectory(last);
        for (int i = 0; i < 1000; i++) File.WriteAllText(Path.Combine(many, i + ".mp3"), "fixture");
        File.WriteAllText(Path.Combine(last, "selected.mp3"), "fixture");
        var picker = new LocalPickerWindow(false) { ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        picker.Show();
        var navigate = typeof(LocalPickerWindow).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance);
        var entriesField = typeof(LocalPickerWindow).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
        var folderField = typeof(LocalPickerWindow).GetField("currentFolder", BindingFlags.NonPublic | BindingFlags.Instance);
        ListBox entries = (ListBox)entriesField.GetValue(picker);
        navigate.Invoke(picker, new object[] { many });
        Assert(!entries.IsEnabled, "Folder scan returns immediately and keeps incomplete entries unavailable");
        PumpUntil(delegate { return entries.IsEnabled && entries.Items.Count == 1000; }, 5000);
        Assert(entries.Items.Count == 1000, "Background folder scan returns all entries");
        VirtualizingTilePanel panel = Descendant<VirtualizingTilePanel>(entries);
        Assert(panel != null && VisualTreeHelper.GetChildrenCount(panel) < 40, "Actual file picker virtualizes a thousand-item folder");
        Pump(250);
        Render((FrameworkElement)picker.Content, Path.Combine(fixtureRoot, "picker.png"));
        navigate.Invoke(picker, new object[] { many });
        navigate.Invoke(picker, new object[] { last });
        PumpUntil(delegate { return entries.IsEnabled && entries.Items.Count == 1; }, 5000);
        Assert((string)folderField.GetValue(picker) == last && entries.Items.Count == 1, "Rapid folder changes show only the latest result");
        navigate.Invoke(picker, new object[] { many });
        picker.Close(); Pump(100);
        Assert(true, "Closing during folder loading cancels safely");
    }

    private static void TestLiveScrub(Slider timeline)
    {
        Image preview = (Image)((Grid)window.Content).FindName("ScrubImage");
        object cache = Field<object>("_scrubPreview");
        PumpUntil(delegate { return preview.Source != null || (bool)cache.GetType().GetProperty("Unavailable").GetValue(cache, null); }, 5000);
        Assert(!(bool)cache.GetType().GetProperty("Unavailable").GetValue(cache, null), "Native frame decoder opens the generated local video");
        Assert(preview.Source != null, "Video frames appear while the drag is still held");
        var latency = new List<double>();
        for (int direction = 0; direction < 2; direction++)
        {
            for (int step = 0; step < 8; step++)
            {
                int frame = direction == 0 ? 40 + step : 47 - step;
                Stopwatch clock = Stopwatch.StartNew();
                timeline.Value = (frame + 0.1) / 5.0;
                // Generous: this asserts the right frame arrives, not how fast. The
                // MEASURE line below reports the actual latency.
                PumpUntil(delegate { return PreviewBlue(preview) == frame * 2; }, 3000);
                latency.Add(clock.Elapsed.TotalMilliseconds);
                Assert(PreviewBlue(preview) == frame * 2, "Slow " + (direction == 0 ? "forward" : "reverse") + " drag displays actual frame " + frame);
            }
        }
        timeline.Value = 8;
        Call("MoveScrubPointer", 1.0, true);
        Assert(Math.Abs(timeline.Value - 8.2) < .0001, "Shift-drag advances one source frame per pixel");
        Call("MoveScrubPointer", -1.0, true);
        Assert(Math.Abs(timeline.Value - 8) < .0001, "Reversing fine drag returns precisely to the prior frame");
        timeline.Value = timeline.Maximum;
        PumpUntil(delegate { return PreviewBlue(preview) == 198; }, 2000);
        Assert(PreviewBlue(preview) == 198 && Field<MediaItem>("_current").IsVideo, "Dragging to the end previews the final frame without advancing the playlist");
        for (int jump = 0; jump < 100; jump++) timeline.Value = jump % 2 == 0 ? 1 : 19;
        timeline.Value = 8;
        PumpUntil(delegate { return PreviewBlue(preview) == 80; }, 2000);
        Assert(PreviewBlue(preview) == 80, "Rapid distant drags converge on the final requested frame");
        Pump(100);
        Assert(PreviewBlue(preview) == 80, "Late decoding results cannot replace the selected frame");
        long bytes = (long)cache.GetType().GetField("cacheBytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache);
        Assert(bytes > 0 && bytes <= 128L * 1024 * 1024, "Frame cache remains bounded in memory");
        latency.Sort();
        Console.WriteLine("MEASURE: live preview frame latency median " + latency[latency.Count / 2].ToString("0.0") + " ms; max " + latency[latency.Count - 1].ToString("0.0") + " ms (50 ms test polling).");
    }

    private static int PreviewBlue(Image image)
    {
        var source = image.Source as BitmapSource;
        if (source == null) return -1;
        byte[] pixel = new byte[4];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return pixel[0];
    }

    private static T Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T) return (T)root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            T result = Descendant<T>(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }
    private delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    private static void TestOpenButtons()
    {
        uint thread = GetCurrentThreadId();
        string[] buttons = { "WelcomeOpenButton", "WelcomeFolderButton", "AddFilesButton", "FolderButton" };
        foreach (string name in buttons)
        {
            string expected = name.Contains("Folder") ? "Open folder" : "Open files";
            bool found = false;
            var closer = new System.Threading.Thread(delegate()
            {
                Stopwatch timeout = Stopwatch.StartNew();
                while (!found && timeout.ElapsedMilliseconds < 8000)
                {
                    EnumThreadWindows(thread, delegate(IntPtr handle, IntPtr unused)
                    {
                        var caption = new StringBuilder(256);
                        var kind = new StringBuilder(64);
                        GetWindowText(handle, caption, caption.Capacity);
                        GetClassName(handle, kind, kind.Capacity);
                        if (kind.ToString() == "#32770" && caption.ToString() == expected)
                        {
                            found = true;
                            PostMessage(handle, 0x111, new IntPtr(2), IntPtr.Zero);
                        }
                        return true;
                    }, IntPtr.Zero);
                    System.Threading.Thread.Sleep(50);
                }
            }) { IsBackground = true };
            closer.Start();
            ((Button)((Grid)window.Content).FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            closer.Join(8500);
            Assert(found, name + " opens the real Windows " + expected.ToLowerInvariant() + " dialog");
            Assert(Field<ObservableCollection<MediaItem>>("_items").Count == 0, "Canceling the native dialog leaves the playlist unchanged");
        }
    }

    private static void TestWindowsPickers()
    {
        Type pickerType = typeof(MainWindow).Assembly.GetType("Trill.WindowsPicker", true);
        MethodInfo create = pickerType.GetMethod("CreateDialog", BindingFlags.Static | BindingFlags.NonPublic);
        Type dialogInterface = pickerType.GetNestedType("IFileOpenDialog", BindingFlags.NonPublic);
        foreach (bool folder in new bool[] { false, true })
        {
            object dialog = create.Invoke(null, new object[] { folder });
            try
            {
                object[] options = new object[] { 0u };
                dialogInterface.GetMethod("GetOptions").Invoke(dialog, options);
                uint flags = (uint)options[0];
                Assert(((flags & 0x20) != 0) == folder, "Native dialog supports the requested file or folder mode");
                Assert(((flags & 0x200) != 0) == !folder, "Native file picker allows multiple files; folder picker chooses one folder");
                Assert((flags & 0x40) != 0 && (flags & 0x1800) == 0x1800, "Native dialog requires existing filesystem items");
                Assert((flags & 0x2000000) != 0, "Native dialog does not add opened items to Windows recent documents");
                if (!folder)
                {
                    object[] index = new object[] { 0u };
                    dialogInterface.GetMethod("GetFileTypeIndex").Invoke(dialog, index);
                    Assert((uint)index[0] == 1, "Native file dialog defaults to the media filter");
                }
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }
        string pattern = (string)typeof(MediaFiles).GetProperty("FileDialogPattern", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
        Assert(pattern.Contains("*.mp4") && pattern.Contains("*.jpg") && pattern.Contains("*.mp3"), "Native file filter includes video, photos, and audio");
        AppSettings initial = AppSettings.CreateDefault();
        Assert(initial.UseWindowsFileDialogs, "Open commands use Windows dialogs by default");
        initial.UseWindowsFileDialogs = false;
        var editor = new SettingsWindow(initial) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        editor.Loaded += delegate
        {
            var toggle = (CheckBox)typeof(SettingsWindow).GetField("windowsFileDialogs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
            toggle.IsChecked = true;
            typeof(SettingsWindow).GetMethod("OnSave", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, new object[] { null, new RoutedEventArgs() });
        };
        Assert(editor.ShowDialog() == true && editor.Result.UseWindowsFileDialogs, "Settings Apply enables Windows dialogs");
        Assert(!initial.UseWindowsFileDialogs, "Picker preference edits do not mutate the original settings");
    }
    private static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
    private static void SetField(string name, object value) { typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value); }
    private static object Call(string name, params object[] arguments) { return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, arguments); }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        assertions++; Console.WriteLine("PASS: " + message);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void PumpUntil(Func<bool> ready, int timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.ElapsedMilliseconds < timeout) Pump(50);
    }
    // 16-bit mono PCM, so the spectrum test has a tone with a known frequency.
    private static void MakeTone(string path, int seconds, double frequency)
    {
        const int rate = 22050;
        int samples = rate * seconds;
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int dataBytes = samples * 2;
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);
            for (int i = 0; i < samples; i++)
                writer.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 12000));
        }
    }

    private static void MakePhoto(string path, byte r, byte g, byte b)
    {
        byte[] pixels = new byte[480 * 270 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i+1] = g; pixels[i+2] = r; pixels[i+3] = 255; }
        var bitmap = BitmapSource.Create(480, 270, 96, 96, PixelFormats.Bgra32, null, pixels, 480 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(path)) encoder.Save(output);
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var render = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 18, 24)), null, rect);

        }
        render.Render(visual);
        render.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
        using (var output = File.Create(path)) encoder.Save(output);
    }

    // Small uncompressed AVI fixture created entirely locally, with no encoder download.
    private static void MakeAvi(string path)
    {
        const int width = 32, height = 32, frameSize = width * height * 3, frames = 100, fps = 5;
        using (var file = File.Create(path))
        using (var w = new BinaryWriter(file))
        {
            Tag(w, "RIFF"); w.Write(0); Tag(w, "AVI ");
            Tag(w, "LIST"); long headerLength = file.Position; w.Write(0); Tag(w, "hdrl");
            Tag(w, "avih"); w.Write(56); w.Write(1000000 / fps); w.Write(frameSize * fps); w.Write(0); w.Write(0x10);
            w.Write(frames); w.Write(0); w.Write(1); w.Write(frameSize); w.Write(width); w.Write(height);
            for (int i = 0; i < 4; i++) w.Write(0);
            Tag(w, "LIST"); w.Write(116); Tag(w, "strl");
            Tag(w, "strh"); w.Write(56); Tag(w, "vids"); Tag(w, "DIB "); w.Write(0); w.Write((short)0); w.Write((short)0);
            w.Write(0); w.Write(1); w.Write(fps); w.Write(0); w.Write(frames); w.Write(frameSize); w.Write(-1); w.Write(0);
            w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
            Tag(w, "strf"); w.Write(40); w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24);
            w.Write(0); w.Write(frameSize); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            long headerEnd = file.Position; file.Position = headerLength; w.Write((int)(headerEnd - headerLength - 4)); file.Position = headerEnd;
            Tag(w, "LIST"); w.Write(4 + frames * (frameSize + 8)); Tag(w, "movi");
            var pixels = new byte[frameSize];
            for (int frame = 0; frame < frames; frame++)
            {
                for (int p = 0; p < pixels.Length; p += 3) { pixels[p] = (byte)(frame * 2); pixels[p+1] = 120; pixels[p+2] = 45; }
                Tag(w, "00db"); w.Write(frameSize); w.Write(pixels);
            }
            Tag(w, "idx1"); w.Write(frames * 16);
            for (int frame = 0; frame < frames; frame++) { Tag(w, "00db"); w.Write(0x10); w.Write(4 + frame * (frameSize + 8)); w.Write(frameSize); }
            long end = file.Position; file.Position = 4; w.Write((int)(end - 8));
        }
    }
    private static void Tag(BinaryWriter writer, string value) { writer.Write(Encoding.ASCII.GetBytes(value)); }
}
