using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private Border _seekLens;
        private readonly Slider _lensSlider = new Slider { IsHitTestVisible = false, Height = 24, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight };
        private readonly TextBlock _lensTime = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White, FontSize = 12 };
        private readonly TextBlock _lensRange = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.LightGray, FontSize = 10 };
        private readonly System.Windows.Controls.Primitives.TickBar _lensTicks = new System.Windows.Controls.Primitives.TickBar { Height = 5, Fill = Brushes.Gray, Placement = System.Windows.Controls.Primitives.TickBarPlacement.Bottom };
        private bool _lensFine;
        private double _lensSpan;

        private void SetupLens()
        {
            var content = new StackPanel(); content.Children.Add(_lensTime); content.Children.Add(_lensSlider); content.Children.Add(_lensTicks); content.Children.Add(_lensRange);
            _seekLens = new Border { Child = content, Width = 340, Padding = new Thickness(12, 8, 12, 8), Background = new SolidColorBrush(Color.FromArgb(245, 33, 33, 33)), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 184, 184, 184)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            Grid.SetRowSpan(_seekLens, 2); Panel.SetZIndex(_seekLens, 50); _root.Children.Add(_seekLens);
        }

        private void ShowLens()
        {
            _lensFine = false;
            _lensSpan = Math.Min(_duration * .15, Math.Max(.5, _current.IsVideo ? _scrubPreview.FrameDuration * 60 : 4));
            _seekLens.Visibility = Visibility.Visible; UpdateLens();
        }

        private void UpdateLens()
        {
            if (_seekLens == null || !_scrubbing) return;
            double value = _seek.Value;
            if (!_lensFine || value < _lensSlider.Minimum || value > _lensSlider.Maximum)
            {
                double start = Math.Max(0, Math.Min(_duration - _lensSpan, value - _lensSpan / 2));
                _lensSlider.Maximum = start + _lensSpan; _lensSlider.Minimum = start;
            }
            _lensSlider.TickFrequency = _current.IsVideo ? Math.Max(.01, _scrubPreview.FrameDuration) : .25;
            _lensTicks.Minimum = _lensSlider.Minimum; _lensTicks.Maximum = _lensSlider.Maximum; _lensTicks.TickFrequency = Math.Max(.001, _lensSpan / 10);
            _lensSlider.Value = value;
            _lensTime.Text = TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            _lensRange.Text = TimeSpan.FromSeconds(_lensSlider.Minimum).ToString(@"mm\:ss\.f") + " — " + TimeSpan.FromSeconds(_lensSlider.Maximum).ToString(@"mm\:ss\.f") + (_lensFine ? "  ·  Fine" : "  ·  Move up for precision");
            Point anchor = _seek.TranslatePoint(new Point(_seek.ActualWidth * value / Math.Max(.001, _duration), 0), _root);
            double left = Math.Max(8, Math.Min(_root.ActualWidth - _seekLens.Width - 8, anchor.X - _seekLens.Width / 2));
            // Stay put once the pointer enters the magnified timeline.
            if (!_lensFine) _seekLens.Margin = new Thickness(left, Math.Max(0, anchor.Y - 88), 0, 0);
        }

        private void MoveLensPointer(double pixels, double vertical, bool shift)
        {
            if (!_lensFine && vertical < -14) _lensFine = true;
            if (_lensFine && !shift) _seek.Value = Math.Max(0, Math.Min(_duration, _seek.Value + pixels * _lensSpan / Math.Max(1, _seekLens.Width - 24)));
            else MoveScrubPointer(pixels, shift);
            UpdateLens();
        }

        private void HideLens() { _lensFine = false; if (_seekLens != null) _seekLens.Visibility = Visibility.Collapsed; }
    }
}
