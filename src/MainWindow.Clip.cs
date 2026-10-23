using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Trill
{
    public sealed partial class MainWindow
    {
        private static readonly Brush MarkBrush = Frozen(Color.FromRgb(0x92, 0xE1, 0xC0));

        private double _loopStart = -1;
        private double _loopEnd = -1;
        private bool _exporting;

        private static Brush Frozen(Color colour)
        {
            SolidColorBrush brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        private bool HasAbLoop { get { return _loopStart >= 0 && _loopEnd > _loopStart; } }

        // One button, three states: set the start, set the end, then clear.
        private void CycleAbLoop()
        {
            if (_current == null || !_current.HasTimeline || !_opened || _failed || _duration <= 0)
            {
                Status("A-B looping applies to video and audio");
                return;
            }
            double at = _scrubbing ? _seek.Value : _video.Position.TotalSeconds;
            if (_loopStart < 0)
            {
                _loopStart = Math.Max(0, Math.Min(at, _duration));
                _loopEnd = -1;
                Status("Loop starts at " + TimeText(_loopStart) + ". Set the end next.");
            }
            else if (_loopEnd < 0)
            {
                if (at <= _loopStart + 0.05)
                {
                    Status("Move past " + TimeText(_loopStart) + " before setting the end");
                    return;
                }
                _loopEnd = Math.Min(at, _duration);
                Status("Looping " + TimeText(_loopStart) + " to " + TimeText(_loopEnd));
            }
            else
            {
                ClearAbLoop(true);
                return;
            }
            UpdateAbLoop();
        }

        private void ClearAbLoop(bool announce)
        {
            bool had = _loopStart >= 0;
            _loopStart = -1;
            _loopEnd = -1;
            if (announce && had) Status("A-B loop cleared");
            UpdateAbLoop();
        }

        private void UpdateAbLoop()
        {
            Button button = Find<Button>("AbLoopButton");
            if (_loopStart < 0)
            {
                button.Content = "A-B";
                button.Foreground = Brushes.Gray;
                button.ToolTip = "Loop a section: set the start, then the end";
            }
            else if (_loopEnd < 0)
            {
                button.Content = "A·";
                button.Foreground = MarkBrush;
                button.ToolTip = "Loop starts at " + TimeText(_loopStart) + ". Click again to set the end.";
            }
            else
            {
                button.Content = "A·B";
                button.Foreground = MarkBrush;
                button.ToolTip = "Looping " + TimeText(_loopStart) + " to " + TimeText(_loopEnd) + ". Click again to clear.";
            }
            LayoutAbMarks();
        }

        // Places the marks in the slider's own geometry, so they line up with the thumb
        // at any width rather than being approximated from the control bounds.
        private void LayoutAbMarks()
        {
            _seek.ApplyTemplate();
            Canvas marks = _seek.Template.FindName("LoopMarks", _seek) as Canvas;
            Border band = _seek.Template.FindName("LoopBand", _seek) as Border;
            Border startMark = _seek.Template.FindName("LoopStartMark", _seek) as Border;
            Border endMark = _seek.Template.FindName("LoopEndMark", _seek) as Border;
            if (marks == null || band == null || startMark == null || endMark == null) return;

            if (_loopStart < 0 || _duration <= 0 || _current == null || !_current.HasTimeline)
            {
                FadeMarks(marks, 0);
                return;
            }

            Track track = _seek.Template.FindName("PART_Track", _seek) as Track;
            double span = track != null ? track.ActualWidth : _seek.ActualWidth;
            double thumb = track != null && track.Thumb != null && track.Thumb.ActualWidth > 0 ? track.Thumb.ActualWidth : 16;
            double usable = Math.Max(1, span - thumb);
            if (span <= 0) return;

            double from = thumb / 2 + Math.Max(0, Math.Min(1, _loopStart / _duration)) * usable;
            startMark.SetValue(Canvas.LeftProperty, from - startMark.Width / 2);

            if (_loopEnd > _loopStart)
            {
                double to = thumb / 2 + Math.Max(0, Math.Min(1, _loopEnd / _duration)) * usable;
                endMark.SetValue(Canvas.LeftProperty, to - endMark.Width / 2);
                endMark.Visibility = Visibility.Visible;
                band.Visibility = Visibility.Visible;
                band.SetValue(Canvas.LeftProperty, from);
                band.Width = Math.Max(0, to - from);
            }
            else
            {
                endMark.Visibility = Visibility.Collapsed;
                band.Visibility = Visibility.Collapsed;
            }
            FadeMarks(marks, 1);
        }

        private static void FadeMarks(Canvas marks, double target)
        {
            if (Math.Abs(marks.Opacity - target) < 0.001) return;
            if (!SystemParameters.ClientAreaAnimation)
            {
                marks.BeginAnimation(UIElement.OpacityProperty, null);
                marks.Opacity = target;
                return;
            }
            DoubleAnimation fade = new DoubleAnimation(target, TimeSpan.FromMilliseconds(target > 0 ? 170 : 120));
            fade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            fade.FillBehavior = FillBehavior.HoldEnd;
            marks.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Driven from the shared tick, so the jump back happens on the same clock that
        // updates the timeline rather than waiting for the media engine to end.
        private void HoldAbLoop()
        {
            if (!HasAbLoop || _scrubbing || !_playing || _failed || !_opened) return;
            if (_current == null || !_current.HasTimeline) return;
            if (_video.Position.TotalSeconds + 0.02 < _loopEnd) return;
            _video.Position = TimeSpan.FromSeconds(_loopStart);
            UpdateTimeline(_loopStart, _duration);
        }

        // ----- frame export -----

        private void ExportFrame()
        {
            if (_current == null || !_current.IsVideo) { Status("Frame export applies to video"); return; }
            if (!_opened || _failed) { Status("Open a video first"); return; }
            if (_exporting) { Status("Still saving the previous frame"); return; }

            double at = _scrubbing ? _seek.Value : _video.Position.TotalSeconds;
            string source = _current.Path;
            _exporting = true;
            Status("Saving frame…");
            // Full-resolution decoding is far too slow for the UI thread.
            ThreadPool.QueueUserWorkItem(delegate
            {
                string outcome = SaveFrame(source, at);
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        _exporting = false;
                        Status(outcome);
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private static string SaveFrame(string source, double seconds)
        {
            NativeVideoReader reader = null;
            try
            {
                // The on-screen preview is deliberately downscaled; an exported still
                // should be the frame at its native size.
                reader = new NativeVideoReader(source, 7680);
                reader.Seek(Math.Max(0, seconds - 0.3));
                VideoFrame chosen = null;
                for (int attempt = 0; attempt < 900 && !reader.Ended; attempt++)
                {
                    VideoFrame frame = reader.Read(0);
                    if (frame == null) continue;
                    chosen = frame;
                    if (frame.Time + frame.Duration > seconds) break;
                }
                if (chosen == null) return "That frame could not be decoded";
                return WritePng(chosen.Bitmap, source, seconds);
            }
            catch (System.Runtime.InteropServices.COMException) { return "That frame could not be decoded"; }
            catch (NotSupportedException) { return "That video cannot be exported frame by frame"; }
            catch (ArgumentException) { return "That frame could not be decoded"; }
            catch (InvalidOperationException) { return "That frame could not be decoded"; }
            catch (OverflowException) { return "That frame is too large to export"; }
            catch (OutOfMemoryException) { return "That frame is too large to export"; }
            catch (DllNotFoundException) { return "Windows media components are unavailable"; }
            catch (EntryPointNotFoundException) { return "Windows media components are unavailable"; }
            finally { if (reader != null) reader.Dispose(); }
        }

        internal static string ExportFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Trill"); }
        }

        private static string WritePng(BitmapSource picture, string source, double seconds)
        {
            try
            {
                Directory.CreateDirectory(ExportFolder);
                string label = CleanName(Path.GetFileNameWithoutExtension(source)) + " " + Stamp(seconds);
                string target = Path.Combine(ExportFolder, label + ".png");
                // CreateNew below still guards against a race; this keeps names tidy.
                for (int copy = 2; File.Exists(target) && copy < 1000; copy++)
                    target = Path.Combine(ExportFolder, label + " (" + copy.ToString(CultureInfo.InvariantCulture) + ").png");
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(picture));
                using (FileStream stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    encoder.Save(stream);
                return "Saved " + Path.GetFileName(target) + " to Pictures\\Trill";
            }
            catch (IOException) { return "Could not write the frame to Pictures\\Trill"; }
            catch (UnauthorizedAccessException) { return "Pictures\\Trill is not writable"; }
            catch (SecurityException) { return "Pictures\\Trill is not writable"; }
            catch (NotSupportedException) { return "Could not write the frame to Pictures\\Trill"; }
            catch (ArgumentException) { return "Could not write the frame to Pictures\\Trill"; }
        }

        private static string Stamp(double seconds)
        {
            double safe = Math.Max(0, seconds);
            int minutes = (int)(safe / 60);
            double rest = safe - minutes * 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + "m"
                + rest.ToString("00.000", CultureInfo.InvariantCulture).Replace('.', 's');
        }

        private static string CleanName(string name)
        {
            if (String.IsNullOrEmpty(name)) return "frame";
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '-');
            name = name.Trim();
            if (name.Length == 0) return "frame";
            return name.Length > 80 ? name.Substring(0, 80).Trim() : name;
        }
    }
}
