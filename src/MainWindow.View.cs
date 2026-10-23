using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private bool _savedTopmost;
        private readonly System.Diagnostics.Stopwatch _fullscreenIdle = new System.Diagnostics.Stopwatch();
        private Point _lastFullscreenPointer;

        private void MediaWheel(object sender, MouseWheelEventArgs e)
        {
            if (_current == null || _current.IsAudio || !_opened || _failed) return;
            ZoomMedia(Math.Pow(1.18, e.Delta / 120.0), e.GetPosition(Find<Grid>("Stage")));
            e.Handled = true;
        }

        private void ZoomMedia(double factor, Point point)
        {
            Grid stage = Find<Grid>("Stage");
            double next = Math.Max(1, Math.Min(8, _zoom * factor));
            double x = point.X - stage.ActualWidth / 2, y = point.Y - stage.ActualHeight / 2;
            double ratio = next / Math.Max(1, _mediaScale.ScaleX);
            SetMediaView(next, x - (x - _mediaPan.X) * ratio, y - (y - _mediaPan.Y) * ratio, true);
            Status(Math.Round(next * 100) + "%");
        }

        private void SetMediaView(double scale, double x, double y, bool animate)
        {
            Grid stage = Find<Grid>("Stage");
            scale = Math.Max(1, Math.Min(8, scale));
            double w = stage.ActualWidth, h = stage.ActualHeight;
            double sourceW = w, sourceH = h;
            if (_current != null && _current.IsVideo && _video.NaturalVideoWidth > 0)
            { sourceW = _video.NaturalVideoWidth; sourceH = _video.NaturalVideoHeight; }
            else if (_photo.Source != null) { sourceW = _photo.Source.Width; sourceH = _photo.Source.Height; }
            double fit = sourceW > 0 && sourceH > 0 ? Math.Min(w / sourceW, h / sourceH) : 1;
            double maxX = Math.Max(0, (sourceW * fit * scale - w) / 2);
            double maxY = Math.Max(0, (sourceH * fit * scale - h) / 2);
            x = Math.Max(-maxX, Math.Min(maxX, x)); y = Math.Max(-maxY, Math.Min(maxY, y));
            _zoom = scale; UpdateZoomControl();
            ViewValue(_mediaScale, ScaleTransform.ScaleXProperty, scale, animate);
            ViewValue(_mediaScale, ScaleTransform.ScaleYProperty, scale, animate);
            ViewValue(_mediaPan, TranslateTransform.XProperty, x, animate);
            ViewValue(_mediaPan, TranslateTransform.YProperty, y, animate);
            stage.Cursor = scale > 1 ? Cursors.Hand : Cursors.Arrow;
        }

        private static void ViewValue(Animatable value, DependencyProperty property, double target, bool animate)
        {
            double from = (double)value.GetValue(property);
            value.BeginAnimation(property, null);
            value.SetValue(property, target);
            if (animate && SystemParameters.ClientAreaAnimation)
                value.BeginAnimation(property, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(90))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void ResetMediaView() { EndMediaPan(); SetMediaView(1, 0, 0, false); }

        private void MediaPointerDown(object sender, MouseButtonEventArgs e)
        {
            if (Find<Button>("ZoomResetButton").IsMouseOver) return;
            if (_current == null || _failed) return;
            if (e.ClickCount == 2) { EndMediaPan(); ToggleFullscreen(); e.Handled = true; return; }
            if (_zoom <= 1 || _current.IsAudio) return;
            Grid stage = Find<Grid>("Stage");
            SetMediaView(_mediaScale.ScaleX, _mediaPan.X, _mediaPan.Y, false);
            _panStart = e.GetPosition(stage); _panOrigin = new Point(_mediaPan.X, _mediaPan.Y);
            _panning = stage.CaptureMouse();
            if (_panning) { stage.Cursor = Cursors.SizeAll; e.Handled = true; }
        }

        private void MediaPointerMove(object sender, MouseEventArgs e)
        {
            if (!_panning || e.LeftButton != MouseButtonState.Pressed) return;
            Point point = e.GetPosition(Find<Grid>("Stage"));
            SetMediaView(_zoom, _panOrigin.X + point.X - _panStart.X, _panOrigin.Y + point.Y - _panStart.Y, false);
            e.Handled = true;
        }

        private void EndMediaPan()
        {
            _panning = false;
            Grid stage = Find<Grid>("Stage");
            if (stage.IsMouseCaptured) stage.ReleaseMouseCapture();
            stage.Cursor = _zoom > 1 ? Cursors.Hand : Cursors.Arrow;
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorData
        { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorData data);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

        private void ToggleFullscreen()
        {
            EndScrub(); EndMediaPan();
            Border bar = Find<Border>("TransportBar");
            if (!_fullscreen)
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                var monitor = new MonitorData { Size = Marshal.SizeOf(typeof(MonitorData)) };
                if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
                _savedState = WindowState;
                _savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                _savedTopmost = Topmost;
                _fullscreen = true;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
                SetGlass(false); Topmost = true;
                Grid.SetRow(bar, 0); bar.VerticalAlignment = VerticalAlignment.Bottom;
                Find<Border>("StageBorder").CornerRadius = new CornerRadius(0);
                ((FrameworkElement)Find<Border>("StageBorder").Parent).Margin = new Thickness(0);
                SetPlaylistVisible();
                NativeRect bounds = monitor.Monitor;
                SetWindowPos(handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x40);
                ShowFullscreenControls();
            }
            else
            {
                _fullscreen = false; _fullscreenIdle.Stop();
                Topmost = _savedTopmost;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
                Left = _savedBounds.Left; Top = _savedBounds.Top; Width = _savedBounds.Width; Height = _savedBounds.Height;
                WindowState = _savedState;
                Grid.SetRow(bar, 1); bar.VerticalAlignment = VerticalAlignment.Stretch;
                bar.Opacity = 1; bar.IsHitTestVisible = true; Cursor = Cursors.Arrow;
                Find<Border>("StageBorder").CornerRadius = new CornerRadius(8);
                ((FrameworkElement)Find<Border>("StageBorder").Parent).Margin = new Thickness(0, 4, 4, 4);
                SetGlass(true); SetPlaylistVisible();
            }
            if (_focusMode) ApplyFocusChrome();
        }

        private void ShowFullscreenControls()
        {
            if (!_fullscreen || _focusMode) return;
            Border bar = Find<Border>("TransportBar");
            bar.Opacity = 1; bar.IsHitTestVisible = true;
            Cursor = Cursors.Arrow;
            Find<Grid>("Stage").Cursor = _zoom > 1 ? Cursors.Hand : Cursors.Arrow;
            _fullscreenIdle.Restart();
        }

        private void TickFullscreen()
        {
            if (_focusMode || !_fullscreen || _fullscreenIdle.Elapsed.TotalSeconds < 2 || Find<Border>("TransportBar").IsMouseOver || _scrubbing || _panning) return;
            Find<Border>("TransportBar").Opacity = 0;
            Find<Border>("TransportBar").IsHitTestVisible = false;
            Cursor = Cursors.None; Find<Grid>("Stage").Cursor = Cursors.None;
        }
    }
}

