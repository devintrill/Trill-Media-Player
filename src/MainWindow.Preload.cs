using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private sealed class WarmMedia
        {
            public MediaItem Item;
            public DateTime Modified;
            public long Length;
            public MediaElement Player;
            public PhotoLoader Loader;
            public BitmapSource Photo;
            public bool Ready;
        }
        private readonly Dictionary<string, WarmMedia> _warmMedia = new Dictionary<string, WarmMedia>(StringComparer.OrdinalIgnoreCase);
        private readonly Image _switchCover = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly DispatcherTimer _switchHandoff = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _warmDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        private bool _warmClosed;

        private void SetupPreloading()
        {
            Find<Grid>("Stage").Children.Insert(1, _switchCover);
            _switchHandoff.Tick += delegate { ClearSwitchCover(); };
            _warmDelay.Tick += delegate { _warmDelay.Stop(); PrepareNeighbors(); };
            Closed += delegate { _warmClosed = true; ClearWarmMedia(); ClearSwitchCover(); };
        }

        private void ConnectPlayer(MediaElement player)
        {
            player.MediaOpened += MediaOpened;
            player.MediaEnded += delegate { if (player == _video && !_scrubbing && _current != null && _current.HasTimeline && _opened) CompleteItem(); };
            player.MediaFailed += delegate { if (player == _video && _current != null && player.Source != null && player.Source.LocalPath == _current.Path) MediaFailed(); };
        }

        private void QueueNeighbors() { if (_warmClosed) return; _warmDelay.Stop(); _warmDelay.Start(); }

        private void PrepareNeighbors()
        {
            if (_warmClosed || _current == null || _failed) return;
            var desired = new List<MediaItem>();
            int index = _items.IndexOf(_current);
            foreach (int direction in new int[] { 1, -1 })
            {
                int neighbor = index + direction;
                if ((_settings.LoopPlaylist || _repeatMode == 2) && _items.Count > 1) neighbor = (neighbor + _items.Count) % _items.Count;
                if (index >= 0 && neighbor >= 0 && neighbor < _items.Count && _items[neighbor] != _current && !desired.Contains(_items[neighbor])) desired.Add(_items[neighbor]);
            }
            foreach (string path in _warmMedia.Keys.ToArray())
                if (!desired.Any(m => m.Path == path)) DropWarm(path);
            foreach (MediaItem item in desired)
            {
                if (_warmMedia.ContainsKey(item.Path) || !MediaFiles.IsLocalPath(item.Path)) continue;
                var slot = new WarmMedia { Item = item };
                try
                {
                    var file = new FileInfo(item.Path); slot.Modified = file.LastWriteTimeUtc; slot.Length = file.Length;
                    _warmMedia[item.Path] = slot;
                    if (!item.HasTimeline)
                    {
                        slot.Loader = new PhotoLoader(Dispatcher);
                        slot.Loader.Load(item.Path, delegate(BitmapSource photo)
                        {
                            WarmMedia current;
                            if (!_warmClosed && _warmMedia.TryGetValue(item.Path, out current) && current == slot) { slot.Photo = photo; slot.Ready = photo != null; }
                        });
                    }
                    else
                    {
                        slot.Player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Uniform, IsMuted = true, Volume = 0, Opacity = 0, IsHitTestVisible = false };
                        Find<Grid>("VisualMedia").Children.Insert(0, slot.Player);
                        slot.Player.MediaOpened += delegate { slot.Ready = true; };
                        slot.Player.MediaFailed += delegate { WarmMedia current; if (_warmMedia.TryGetValue(item.Path, out current) && current == slot) DropWarm(item.Path); };
                        slot.Player.Source = new Uri(item.Path); slot.Player.Pause();
                    }
                }
                catch (IOException) { DropWarm(item.Path); }
                catch (UnauthorizedAccessException) { DropWarm(item.Path); }
                catch (NotSupportedException) { DropWarm(item.Path); }
            }
        }

        private WarmMedia TakeWarm(MediaItem item)
        {
            WarmMedia slot;
            if (!_warmMedia.TryGetValue(item.Path, out slot) || !slot.Ready) return null;
            try
            {
                var file = new FileInfo(item.Path);
                if (!MediaFiles.IsLocalPath(item.Path) || !file.Exists || file.Length != slot.Length || file.LastWriteTimeUtc != slot.Modified) { DropWarm(item.Path); return null; }
            }
            catch (IOException) { DropWarm(item.Path); return null; }
            catch (UnauthorizedAccessException) { DropWarm(item.Path); return null; }
            _warmMedia.Remove(item.Path);
            if (slot.Loader != null) slot.Loader.Dispose();
            return slot;
        }

        private void DropWarm(string path)
        {
            WarmMedia slot;
            if (!_warmMedia.TryGetValue(path, out slot)) return;
            _warmMedia.Remove(path);
            if (slot.Loader != null) slot.Loader.Dispose();
            if (slot.Player != null) { slot.Player.Close(); slot.Player.Source = null; Find<Grid>("VisualMedia").Children.Remove(slot.Player); }
            slot.Photo = null;
        }

        private void ClearWarmMedia() { _warmDelay.Stop(); foreach (string path in _warmMedia.Keys.ToArray()) DropWarm(path); }

        private void HoldSwitchFrame(MediaItem next)
        {
            _switchHandoff.Stop();
            if (next.IsAudio) { ClearSwitchCover(); return; }
            if (!_opened || _current == null || _current.IsAudio) return;
            if (_photo.Source != null) _switchCover.Source = _photo.Source;
            else if (_current.IsVideo && _video.ActualWidth > 0 && _video.ActualHeight > 0)
            {
                double scale = Math.Min(1, 1280 / Math.Max(_video.ActualWidth, _video.ActualHeight));
                var bitmap = new RenderTargetBitmap(Math.Max(1, (int)(_video.ActualWidth * scale)), Math.Max(1, (int)(_video.ActualHeight * scale)), 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (DrawingContext drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(_video), null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                bitmap.Render(visual); bitmap.Freeze(); _switchCover.Source = bitmap;
            }
            _switchCover.Visibility = _switchCover.Source == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearSwitchCover() { _switchHandoff.Stop(); _switchCover.Source = null; _switchCover.Visibility = Visibility.Collapsed; }
    }
}
