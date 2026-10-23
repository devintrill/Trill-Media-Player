using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private ScrubPreview _hoverPreview;
        private readonly DispatcherTimer _hoverDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly MediaElement _hoverProbe = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Volume = 0, Opacity = 0, IsHitTestVisible = false, Width = 1, Height = 1 };
        private readonly Image _hoverImage = new Image { Stretch = Stretch.UniformToFill };
        private Border _hoverSurface;
        private SidebarEntry _hoverEntry;
        private Border _hoverThumbnail;
        private double _hoverRatio, _hoverDuration;

        private void SetupHover()
        {
            _hoverPreview = new ScrubPreview(Dispatcher, RenderHover, 8L * 1024 * 1024, 320);
            _hoverSurface = new Border { Child = _hoverImage, Background = Brushes.Black, ClipToBounds = true, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(_hoverSurface, 2); Panel.SetZIndex(_hoverSurface, 40);
            _root.Children.Add(_hoverSurface); _root.Children.Add(_hoverProbe);
            _playlist.MouseMove += HoverMoved;
            _playlist.SizeChanged += delegate { StopHover(); };
            _playlist.MouseLeave += delegate { StopHover(); };
            _playlist.PreviewMouseDown += delegate { StopHover(); };
            _playlist.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { StopHover(); }));
            _hoverDelay.Tick += delegate { _hoverDelay.Stop(); BeginHover(); };
            _hoverProbe.MediaOpened += delegate
            {
                if (_hoverEntry == null || _hoverProbe.Source == null || _hoverProbe.Source.LocalPath != _hoverEntry.Path) return;
                _hoverDuration = _hoverProbe.NaturalDuration.HasTimeSpan ? _hoverProbe.NaturalDuration.TimeSpan.TotalSeconds : 0;
                _hoverProbe.Close(); _hoverProbe.Source = null;
                if (_hoverDuration > 0) _hoverPreview.Start(_hoverEntry.Path, Math.Min(_hoverDuration - .00001, _hoverRatio * _hoverDuration));
            };
            _hoverProbe.MediaFailed += delegate { StopHover(); };
            Closed += delegate { StopHover(); _hoverPreview.Dispose(); };
        }

        private static T VisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T found = child as T ?? VisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void HoverMoved(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || _scrubbing) { StopHover(); return; }
            var hit = _playlist.InputHitTest(e.GetPosition(_playlist)) as DependencyObject;
            var row = hit == null ? null : ItemsControl.ContainerFromElement(_playlist, hit) as ListBoxItem;
            var entry = row == null ? null : row.Content as SidebarEntry;
            Image thumbnail = row == null ? null : VisualChild<Image>(row);
            Border border = thumbnail == null ? null : VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(thumbnail)) as Border;
            if (entry == null || entry.Media == null || !entry.Media.IsVideo || border == null || !new Rect(0, 0, border.ActualWidth, border.ActualHeight).Contains(e.GetPosition(border))) { StopHover(); return; }
            if (_hoverEntry != entry)
            {
                StopHover(); _hoverEntry = entry; _hoverThumbnail = border; _hoverDelay.Start();
            }
            _hoverRatio = Math.Max(0, Math.Min(1, e.GetPosition(border).X / Math.Max(1, border.ActualWidth)));
            if (_hoverDuration > 0) _hoverPreview.Request(Math.Min(_hoverDuration - .00001, _hoverRatio * _hoverDuration));
        }

        private void BeginHover()
        {
            if (_hoverEntry == null || !MediaFiles.IsLocalPath(_hoverEntry.Path)) return;
            _hoverProbe.Source = new Uri(_hoverEntry.Path); _hoverProbe.Pause();
        }

        private void RenderHover()
        {
            if (_hoverEntry == null || _hoverThumbnail == null || !_hoverThumbnail.IsVisible) return;
            VideoFrame frame = _hoverPreview.CurrentFrame();
            if (frame == null) return;
            Point point = _hoverThumbnail.TranslatePoint(new Point(), _root);
            _hoverSurface.Margin = new Thickness(point.X, point.Y, 0, 0);
            _hoverSurface.Width = _hoverThumbnail.ActualWidth; _hoverSurface.Height = _hoverThumbnail.ActualHeight;
            _hoverImage.Source = frame.Bitmap; _hoverSurface.Visibility = Visibility.Visible;
        }

        private void StopHover()
        {
            _hoverDelay.Stop(); _hoverEntry = null; _hoverThumbnail = null; _hoverDuration = 0;
            if (_hoverPreview != null) _hoverPreview.Cancel();
            _hoverProbe.Close(); _hoverProbe.Source = null;
            _hoverImage.Source = null;
            if (_hoverSurface != null) _hoverSurface.Visibility = Visibility.Collapsed;
        }
    }
}
